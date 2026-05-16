using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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
public class OrchestratorE2ETests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly AntiphonAppFixture _appFixture = new();
    private readonly PlaywrightFixture _playwrightFixture = new();

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
    }

    [Test]
    public async Task Orchestrator_user_can_view_snapshot_and_pause_resume()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var projectId = await CreateProjectAsync($"E13 Browser Project {suffix}");
        var boardId = await CreateBoardAsync(projectId, $"E13 Browser Board {suffix}");
        var board = await GetBoardAsync(boardId);
        var activeColumnId = GetColumnId(board, "in-progress");
        var runningCard = await CreateCardAsync(boardId, activeColumnId, $"E13 Running {suffix}");
        var retryCard = await CreateCardAsync(boardId, activeColumnId, $"E13 Retry {suffix}");
        await SeedOrchestratorStateAsync(runningCard.Id, retryCard.Id);

        var (page, context) = await _playwrightFixture.NewPageAsync();
        var passed = false;
        try
        {
            var response = await page.GotoAsync($"{_appFixture.PlaywrightAddress}/orchestrator");
            response.ShouldNotBeNull();
            response!.Status.ShouldBeLessThan(500);
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

            await Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions
            {
                Name = "Orchestrator"
            })).ToBeVisibleAsync();
            await Expect(page.GetByText(runningCard.Identifier, new PageGetByTextOptions
            {
                Exact = true
            })).ToBeVisibleAsync();
            await Expect(page.GetByText(retryCard.Identifier, new PageGetByTextOptions
            {
                Exact = true
            })).ToBeVisibleAsync();
            await Expect(page.GetByText("temporary E13 failure")).ToBeVisibleAsync();

            var pauseResponse = await page.RunAndWaitForResponseAsync(
                () => page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Pause" }).ClickAsync(),
                response => response.Url.Contains("/api/orchestrator/pause", StringComparison.Ordinal)
                    && response.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase));
            pauseResponse.Status.ShouldBe(200);
            await Expect(page.GetByText("Paused", new PageGetByTextOptions { Exact = true }))
                .ToBeVisibleAsync();

            var resumeResponse = await page.RunAndWaitForResponseAsync(
                () => page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Resume" }).ClickAsync(),
                response => response.Url.Contains("/api/orchestrator/resume", StringComparison.Ordinal)
                    && response.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase));
            resumeResponse.Status.ShouldBe(200);
            await Expect(page.GetByText("Running", new PageGetByTextOptions { Exact = true }))
                .ToBeVisibleAsync();

            passed = true;
        }
        finally
        {
            await PlaywrightFixture.CaptureOnCompletionAsync(page, passed);
            await context.DisposeAsync();
        }
    }

    private async Task<Guid> CreateProjectAsync(string name)
    {
        var response = await _appFixture.HttpClient.PostAsJsonAsync(
            "/api/projects",
            new
            {
                name,
                gitRepositoryUrl = "https://github.com/example/e13-orchestrator-e2e.git",
                localRepositoryPath = (string?)null,
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

    private async Task<CardSeed> CreateCardAsync(Guid boardId, Guid boardColumnId, string title)
    {
        var response = await _appFixture.HttpClient.PostAsJsonAsync(
            $"/api/boards/{boardId}/cards",
            new
            {
                boardColumnId,
                title,
                description = "Visible from the orchestrator dashboard.",
                labels = new[] { "e2e", "orchestrator" }
            },
            JsonOptions);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return new CardSeed(
            body.GetProperty("id").GetGuid(),
            body.GetProperty("identifier").GetString()
                ?? throw new InvalidOperationException("Card identifier missing."));
    }

    private async Task SeedOrchestratorStateAsync(Guid runningCardId, Guid retryCardId)
    {
        using var scope = _appFixture.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var sessionId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            CardId = runningCardId,
            DefinitionName = "e13-e2e",
            AgentKind = AgentKind.Raw,
            Status = SessionStatus.Running,
            Cwd = "D:/e13-e2e",
            CreatedAt = now.AddMinutes(-3),
            StartedAt = now.AddMinutes(-3),
            LastSeenAt = now.AddSeconds(-5)
        });
        db.RunAttempts.Add(new RunAttempt
        {
            Id = attemptId,
            CardId = runningCardId,
            AgentSessionId = sessionId,
            AttemptNumber = 1,
            Phase = RunPhase.StreamingTurn,
            CreatedAt = now.AddMinutes(-3),
            StartedAt = now.AddMinutes(-3),
            LastEventAt = now.AddSeconds(-5),
            PhaseStartedAt = now.AddMinutes(-3),
            Prompt = "hidden prompt text",
            TokenUsage = new TokenUsage
            {
                Id = Guid.NewGuid(),
                RunAttemptId = attemptId,
                TokensIn = 90,
                TokensOut = 10,
                CostUsd = 0.05m,
                ModelName = "e13-e2e",
                CreatedAt = now
            }
        });
        db.RetrySchedules.Add(new RetrySchedule
        {
            Id = Guid.NewGuid(),
            CardId = retryCardId,
            AttemptCount = 1,
            MaxAttempts = 3,
            NextRetryAt = now.AddSeconds(-30),
            LastAttemptAt = now.AddMinutes(-5),
            LastError = "temporary E13 failure"
        });
        await db.SaveChangesAsync();
    }

    private static Guid GetColumnId(JsonElement board, string stateKey)
    {
        return board.GetProperty("columns")
            .EnumerateArray()
            .Single(column => column.GetProperty("stateKey").GetString() == stateKey)
            .GetProperty("id")
            .GetGuid();
    }

    private sealed record CardSeed(Guid Id, string Identifier);
}
