using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.E2E.Fixtures;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Shouldly;
using TUnit.Core;
using static Microsoft.Playwright.Assertions;

namespace Antiphon.E2E;

[NotInParallel]
public class AgentE2ETests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
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
        var templateId = await CreateWorkflowTemplateAsync($"E2E Agent Queue {suffix}");
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
            await createDialog.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Name" }).FillAsync(agentName);
            await createDialog.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Working directory" }).FillAsync(workingDirectory);
            await createDialog.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Details" }).FillAsync("Created through the agents E2E flow.");
            var createResponse = await page.RunAndWaitForResponseAsync(
                () => createDialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Create" }).ClickAsync(),
                apiResponse => apiResponse.Url.EndsWith("/api/agents", StringComparison.Ordinal)
                    && apiResponse.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase));
            if (createResponse.Status >= 300)
            {
                var responseBody = await createResponse.TextAsync();
                throw new InvalidOperationException(
                    $"Agent creation failed with HTTP {createResponse.Status}: {responseBody}");
            }

            using var createBody = JsonDocument.Parse(await createResponse.TextAsync());
            var agentId = createBody.RootElement.GetProperty("id").GetGuid();
            await SetAgentDefaultWorkflowTemplateAsync(agentId, templateId);

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
            await Expect(queueRow).ToContainTextAsync(agentName);
            await Expect(queueRow).ToContainTextAsync("Implement");

            await AssertCardAssignedAsync(cardId, templateId);
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

    private async Task<Guid> CreateWorkflowTemplateAsync(string name)
    {
        using var scope = _appFixture.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var templateId = Guid.NewGuid();
        db.WorkflowTemplates.Add(new WorkflowTemplate
        {
            Id = templateId,
            Name = name,
            Description = "Agents page E2E default workflow",
            YamlDefinition = """
                name: One Shot
                description: Implement then review
                stages:
                  - name: Implement
                    executorType: agent
                    gateRequired: false
                  - name: Human Review
                    executorType: human
                    gateRequired: true
                """,
            IsBuiltIn = false,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
        return templateId;
    }

    private async Task SetAgentDefaultWorkflowTemplateAsync(Guid agentId, Guid templateId)
    {
        using var scope = _appFixture.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var agent = await db.Agents.SingleAsync(a => a.Id == agentId);
        agent.DefaultWorkflowTemplateId = templateId;
        agent.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private async Task AssertCardAssignedAsync(Guid cardId, Guid templateId)
    {
        using var scope = _appFixture.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var card = await db.Cards
            .Include(c => c.ActiveWorkflowRun)!.ThenInclude(r => r!.CurrentStage)
            .SingleAsync(c => c.Id == cardId);

        card.AssignedAgentId.ShouldNotBeNull();
        card.AgentQueuePosition.ShouldBe(1);
        card.ActiveWorkflowRun.ShouldNotBeNull();
        card.ActiveWorkflowRun!.WorkflowTemplateId.ShouldBe(templateId);
        card.ActiveWorkflowRun.WorkflowDefinitionSnapshot.ShouldContain("name: One Shot");
        card.ActiveWorkflowRun!.CurrentStage.ShouldNotBeNull();
        card.ActiveWorkflowRun.CurrentStage!.Name.ShouldBe("Implement");
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
