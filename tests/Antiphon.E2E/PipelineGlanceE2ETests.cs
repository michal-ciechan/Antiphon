using System.Text.Json;
using Antiphon.E2E.Fixtures;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Shouldly;
using TUnit.Core;
using static Microsoft.Playwright.Assertions;

namespace Antiphon.E2E;

[NotInParallel]
[ParallelLimiter<ProcessSpawnLimit>]
[Category("OptIn")]
public class PipelineGlanceE2ETests
{
    private readonly AntiphonAppFixture _app = new() { UsePrebuiltFrontend = true };
    private readonly PlaywrightFixture _browser = new();

    [Before(Test)]
    public async Task SetupAsync()
    {
        await _app.InitializeAsync();
        await _browser.InitializeAsync();
    }

    [After(Test)]
    public async Task TeardownAsync()
    {
        await _browser.DisposeAsync();
        await _app.DisposeAsync();
    }

    [Test]
    public async Task C557_combined_glance_on_phone()
    {
        var (boardId, cardIds, taskId) = await SeedAsync();
        using var response = await _app.HttpClient.GetAsync("/api/agent-tasks/pipeline");
        response.EnsureSuccessStatusCode();
        using var contract = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var backlog = contract.RootElement.GetProperty("investigateBacklog");
        backlog.GetProperty("total").GetInt32().ShouldBe(7);
        var expected = backlog.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("cardId").GetGuid()).ToArray();
        expected.ShouldBe(cardIds.Take(5));

        var (page, context) = await _browser.NewPageAsync();
        var passed = false;
        try
        {
            foreach (var width in new[] { 390, 1280 })
            {
                await page.SetViewportSizeAsync(new ViewportSize { Width = width, Height = 844 });
                var navigation = await page.GotoAsync($"{_app.PlaywrightAddress}/orchestrator?tab=pipeline");
                navigation.ShouldNotBeNull();
                navigation!.Status.ShouldBeLessThan(500);
                await Expect(page.GetByText("Backlog candidates", new PageGetByTextOptions { Exact = true }))
                    .ToBeVisibleAsync();
                await Expect(page.GetByText("7 total · top 5", new PageGetByTextOptions { Exact = true }))
                    .ToBeVisibleAsync();
                await Expect(page.GetByText("Ranked by card priority; dispatch is a decision"))
                    .ToBeVisibleAsync();
                var links = page.Locator("[data-testid^='pipeline-row-candidate:']");
                (await links.CountAsync()).ShouldBe(5);
                for (var i = 0; i < expected.Length; i++)
                {
                    var link = links.Nth(i);
                    (await link.GetAttributeAsync("href")).ShouldBe($"/boards/{boardId}?card={expected[i]}");
                    var box = await link.BoundingBoxAsync();
                    box.ShouldNotBeNull();
                    box!.X.ShouldBeGreaterThanOrEqualTo(0);
                    (box.X + box.Width).ShouldBeLessThanOrEqualTo(width + 1);
                }
                (await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"))
                    .ShouldBeTrue();
                await Expect(page.Locator("[data-testid='pipeline-stage-Investigate']")).ToBeVisibleAsync();
                await Expect(page.Locator("[data-testid='pipeline-stage-Code']")).ToBeVisibleAsync();
                await Expect(page.Locator("[data-testid='pipeline-stage-Review']")).ToBeVisibleAsync();
                await Expect(page.Locator("[data-testid='pipeline-stage-Mutation']")).ToBeVisibleAsync();
                await PlaywrightFixture.CapturePageAsync(page, $"c557_{width}");
            }

            await page.Locator($"[data-testid='pipeline-row-{taskId}']").ClickAsync();
            await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex($"task={taskId}"));
            await page.GotoAsync($"{_app.PlaywrightAddress}/orchestrator?tab=pipeline");
            await page.Locator("[data-testid^='pipeline-row-candidate:']").First.ClickAsync();
            await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex($"/boards/{boardId}\\?card={cardIds[0]}"));
            passed = true;
        }
        finally
        {
            await PlaywrightFixture.CaptureOnCompletionAsync(page, passed);
            await context.DisposeAsync();
        }
    }

    private async Task<(Guid BoardId, Guid[] CardIds, Guid TaskId)> SeedAsync()
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
        var projectId = Guid.NewGuid(); var boardId = Guid.NewGuid();
        var backlogColumn = Guid.NewGuid(); var reviewColumn = Guid.NewGuid();
        db.Projects.Add(new Project { Id = projectId, Name = "C557 glance", GitRepositoryUrl = "https://example.test/c557.git", CreatedAt = now, UpdatedAt = now });
        db.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "C557 glance", CreatedAt = now, UpdatedAt = now });
        db.BoardColumns.AddRange(
            new BoardColumn { Id = backlogColumn, BoardId = boardId, StateKey = "backlog", Name = "Backlog", CardStatus = CardStatus.Backlog, ColumnOrder = 0, CreatedAt = now, UpdatedAt = now },
            new BoardColumn { Id = reviewColumn, BoardId = boardId, StateKey = "review", Name = "Review", CardStatus = CardStatus.Review, ColumnOrder = 1, CreatedAt = now, UpdatedAt = now });
        var ids = Enumerable.Range(0, 7).Select(_ => Guid.NewGuid()).ToArray();
        for (var i = 0; i < ids.Length; i++)
        {
            db.Cards.Add(new Card
            {
                Id = ids[i], BoardId = boardId, BoardColumnId = backlogColumn,
                Identifier = $"CARD-{i + 1:0000}", Title = $"Candidate {i + 1} with a deliberately long title that must fit the phone viewport without horizontal scrolling or clipping the rank",
                Status = CardStatus.Backlog, Importance = i == 0 ? CardImportance.Critical : CardImportance.Normal,
                Position = i + 1, CreatedAt = now.AddDays(-20), UpdatedAt = now,
            });
        }
        var formalId = Guid.NewGuid();
        db.Cards.Add(new Card { Id = formalId, BoardId = boardId, BoardColumnId = reviewColumn,
            Identifier = "CARD-9000", Title = "Formal ready", Status = CardStatus.Review,
            Importance = CardImportance.High, CreatedAt = now.AddDays(-20), UpdatedAt = now });
        var sourceId = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = sourceId, RootTaskId = sourceId, Role = AgentTaskRole.Plan,
            Status = AgentTaskStatus.Succeeded, Title = "formal source", Goal = "formal source",
            CardId = formalId, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = "/tmp/c557", CreatedAt = now.AddDays(-2),
            DispatchedAt = now.AddDays(-2), CompletedAt = now.AddDays(-1),
            NextStage = PipelineHandoffKind.Investigate,
        });
        var taskId = Guid.NewGuid();
        foreach (var (role, status, id) in new[]
        {
            (AgentTaskRole.Code, AgentTaskStatus.Working, taskId),
            (AgentTaskRole.Review, AgentTaskStatus.Queued, Guid.NewGuid()),
            (AgentTaskRole.Mutation, AgentTaskStatus.Blocked, Guid.NewGuid()),
        })
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = id, RootTaskId = id, Role = role, Status = status,
                Title = $"{role} task", Goal = $"{role} task", Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = "/tmp/c557", CreatedAt = now.AddDays(-1),
                DispatchedAt = status == AgentTaskStatus.Working ? now.AddHours(-1) : null,
            });
        }
        await db.SaveChangesAsync();
        return (boardId, ids, taskId);
    }
}
