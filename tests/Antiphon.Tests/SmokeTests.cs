using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Shouldly;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;
using Testcontainers.PostgreSql;

namespace Antiphon.Tests;

/// <summary>
/// Smoke tests that verify the app starts and critical endpoints respond.
/// One shared testcontainer + factory per class; tests run sequentially within the class.
/// </summary>
[NotInParallel(nameof(SmokeTests))]
public class SmokeTests
{
    private static readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("antiphon_smoke")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _client = null!;

    [Before(Class)]
    public static async Task SetupClassAsync()
    {
        await _container.StartAsync();

        // Set env var before factory builds so WebApplication.CreateBuilder sees it —
        // affects both the DbContext registration and the AddNpgSql health check.
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection",
            _container.GetConnectionString());
        Environment.SetEnvironmentVariable("GitHub__Enabled", "false");

        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    [After(Class)]
    public static async Task TeardownClassAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _container.DisposeAsync();
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
        Environment.SetEnvironmentVariable("GitHub__Enabled", null);
    }

    [Test]
    public async Task Health_endpoint_returns_healthy()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.ShouldBe("Healthy");
    }

    [Test]
    public async Task App_starts_successfully()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.ShouldNotBe(HttpStatusCode.InternalServerError);
    }

    [Test]
    public async Task Sessions_buffer_unknown_session_returns_not_found()
    {
        var response = await _client.GetAsync($"/api/sessions/{Guid.NewGuid()}/buffer");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var content = await response.Content.ReadAsStringAsync();
        content.ShouldContain("AgentSession");
    }

    [Test]
    public async Task Sessions_resize_rejects_non_positive_terminal_size()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/sessions/{Guid.NewGuid()}/resize",
            new { cols = 0, rows = 30 });

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        var content = await response.Content.ReadAsStringAsync();
        content.ShouldContain("Terminal cols and rows must be positive.");
    }

    [Test]
    public async Task Orchestrator_pause_resume_api_updates_state()
    {
        var pause = await _client.PostAsync("/api/orchestrator/pause", content: null);
        pause.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await pause.Content.ReadAsStringAsync()).ShouldContain("\"paused\":true");

        var pausedState = await _client.GetAsync("/api/orchestrator/state");
        pausedState.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await pausedState.Content.ReadAsStringAsync()).ShouldContain("\"paused\":true");

        var resume = await _client.PostAsync("/api/orchestrator/resume", content: null);
        resume.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await resume.Content.ReadAsStringAsync()).ShouldContain("\"paused\":false");
    }

    [Test]
    public async Task OrchestratorStateApi_snapshot_includes_running_and_retry()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var projectResponse = await _client.PostAsJsonAsync("/api/projects", new
        {
            name = $"E13 State Project {suffix}",
            gitRepositoryUrl = "https://github.com/example/e13-state.git",
            localRepositoryPath = (string?)null,
            baseBranch = "main",
            constitutionPath = (string?)null,
            gitHubIntegrationEnabled = false,
            notificationsEnabled = false
        });
        projectResponse.EnsureSuccessStatusCode();
        var project = await projectResponse.Content.ReadFromJsonAsync<JsonElement>();
        var projectId = project.GetProperty("id").GetGuid();

        var boardResponse = await _client.PostAsJsonAsync("/api/boards", new
        {
            projectId,
            name = $"E13 State Board {suffix}"
        });
        boardResponse.EnsureSuccessStatusCode();
        var board = await boardResponse.Content.ReadFromJsonAsync<JsonElement>();
        var boardId = board.GetProperty("id").GetGuid();
        var activeColumnId = board.GetProperty("columns")
            .EnumerateArray()
            .Single(c => c.GetProperty("stateKey").GetString() == "in-progress")
            .GetProperty("id")
            .GetGuid();

        var runningCardResponse = await _client.PostAsJsonAsync($"/api/boards/{boardId}/cards", new
        {
            boardColumnId = activeColumnId,
            title = $"E13 Running {suffix}",
            description = "Running snapshot card",
            labels = new[] { "e13" }
        });
        runningCardResponse.EnsureSuccessStatusCode();
        var runningCard = await runningCardResponse.Content.ReadFromJsonAsync<JsonElement>();
        var runningCardId = runningCard.GetProperty("id").GetGuid();

        var retryCardResponse = await _client.PostAsJsonAsync($"/api/boards/{boardId}/cards", new
        {
            boardColumnId = activeColumnId,
            title = $"E13 Retry {suffix}",
            description = "Retry snapshot card",
            labels = new[] { "e13" }
        });
        retryCardResponse.EnsureSuccessStatusCode();
        var retryCard = await retryCardResponse.Content.ReadFromJsonAsync<JsonElement>();
        var retryCardId = retryCard.GetProperty("id").GetGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            var sessionId = Guid.NewGuid();
            var attemptId = Guid.NewGuid();
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                CardId = runningCardId,
                DefinitionName = "e13-raw",
                AgentKind = AgentKind.Raw,
                Status = SessionStatus.Running,
                Cwd = "D:/e13",
                CreatedAt = now.AddMinutes(-2),
                StartedAt = now.AddMinutes(-2),
                LastSeenAt = now.AddSeconds(-5)
            });
            db.RunAttempts.Add(new RunAttempt
            {
                Id = attemptId,
                CardId = runningCardId,
                AgentSessionId = sessionId,
                AttemptNumber = 2,
                Phase = RunPhase.StreamingTurn,
                CreatedAt = now.AddMinutes(-2),
                StartedAt = now.AddMinutes(-2),
                LastEventAt = now.AddSeconds(-5),
                PhaseStartedAt = now.AddMinutes(-1),
                Prompt = "observe state",
                TokenUsage = new TokenUsage
                {
                    Id = Guid.NewGuid(),
                    RunAttemptId = attemptId,
                    TokensIn = 123,
                    TokensOut = 45,
                    CostUsd = 0.12m,
                    ModelName = "e13-test",
                    CreatedAt = now
                }
            });
            db.RetrySchedules.Add(new RetrySchedule
            {
                Id = Guid.NewGuid(),
                CardId = retryCardId,
                AttemptCount = 1,
                MaxAttempts = 3,
                NextRetryAt = now.AddSeconds(-10),
                LastAttemptAt = now.AddMinutes(-5),
                LastError = "retry me"
            });
            await db.SaveChangesAsync();
        }

        var stateResponse = await _client.GetAsync("/api/orchestrator/state");
        stateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var state = await stateResponse.Content.ReadFromJsonAsync<JsonElement>();

        state.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        state.GetProperty("runningSessions").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        state.GetProperty("retryQueueLength").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        state.GetProperty("totals").GetProperty("tokensIn").GetInt64().ShouldBeGreaterThanOrEqualTo(123);
        state.GetProperty("limits").GetProperty("maxDispatchesPerTick").GetInt32().ShouldBeGreaterThan(0);
        state.GetProperty("running").EnumerateArray()
            .ShouldContain(item => item.GetProperty("cardId").GetGuid() == runningCardId
                && item.GetProperty("turnCount").GetInt32() == 1
                && item.GetProperty("phase").GetString() == "StreamingTurn");
        state.GetProperty("retryQueue").EnumerateArray()
            .ShouldContain(item => item.GetProperty("cardId").GetGuid() == retryCardId
                && item.GetProperty("lastError").GetString() == "retry me");
        state.GetRawText().ShouldNotContain("observe state");
    }

    [Test]
    public async Task Board_and_agent_endpoints_respond()
    {
        var agents = await _client.GetAsync("/api/agents");
        agents.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await agents.Content.ReadAsStringAsync()).ShouldStartWith("[");

        var agentDefinitions = await _client.GetAsync("/api/agents/definitions");
        agentDefinitions.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await agentDefinitions.Content.ReadAsStringAsync()).ShouldContain("\"defaultDefinition\"");

        var boards = await _client.GetAsync("/api/boards");
        boards.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await boards.Content.ReadAsStringAsync()).ShouldStartWith("[");
    }

    [Test]
    public async Task AgentEndpoint_post_agent_and_get_agents_round_trips_created_agent()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var createResponse = await _client.PostAsJsonAsync("/api/agents", new
        {
            name = $"Endpoint Agent {suffix}",
            workingDirectory = $"D:/src/agent-{suffix}",
            details = "Created by endpoint smoke test"
        });

        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var agentId = created.GetProperty("id").GetGuid();
        created.GetProperty("name").GetString().ShouldBe($"Endpoint Agent {suffix}");
        created.GetProperty("queue").EnumerateArray().ShouldBeEmpty();

        var listResponse = await _client.GetAsync("/api/agents");

        listResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var agents = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        agents.EnumerateArray()
            .ShouldContain(agent => agent.GetProperty("id").GetGuid() == agentId
                && agent.GetProperty("name").GetString() == $"Endpoint Agent {suffix}");
    }

    [Test]
    public async Task Board_card_move_api_requires_concurrency_token_and_round_trips_card()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-board-api-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var projectResponse = await _client.PostAsJsonAsync("/api/projects", new
            {
                name = $"Board API {Guid.NewGuid():N}",
                gitRepositoryUrl = "https://github.com/example/board-api.git",
                localRepositoryPath = tempRoot,
                baseBranch = "main",
                constitutionPath = (string?)null,
                gitHubIntegrationEnabled = false,
                notificationsEnabled = false
            });
            projectResponse.EnsureSuccessStatusCode();
            var projectJson = await projectResponse.Content.ReadFromJsonAsync<JsonElement>();
            var projectId = projectJson.GetProperty("id").GetGuid();

            var boardResponse = await _client.PostAsJsonAsync("/api/boards", new
            {
                projectId,
                name = "Endpoint Board"
            });
            boardResponse.EnsureSuccessStatusCode();
            var boardJson = await boardResponse.Content.ReadFromJsonAsync<JsonElement>();
            var boardId = boardJson.GetProperty("id").GetGuid();

            var cardResponse = await _client.PostAsJsonAsync($"/api/boards/{boardId}/cards", new
            {
                title = "Move through HTTP"
            });
            cardResponse.EnsureSuccessStatusCode();
            var cardJson = await cardResponse.Content.ReadFromJsonAsync<JsonElement>();
            var cardId = cardJson.GetProperty("id").GetGuid();
            var columnId = cardJson.GetProperty("boardColumnId").GetGuid();
            var concurrencyToken = cardJson.GetProperty("concurrencyToken").GetGuid();

            var missingToken = await _client.PatchAsJsonAsync($"/api/cards/{cardId}", new
            {
                boardColumnId = columnId
            });
            missingToken.StatusCode.ShouldBe((HttpStatusCode)422);
            (await missingToken.Content.ReadAsStringAsync()).ShouldContain("Card concurrency token is required.");

            var staleToken = await _client.PatchAsJsonAsync($"/api/cards/{cardId}", new
            {
                boardColumnId = columnId,
                concurrencyToken = Guid.NewGuid()
            });
            staleToken.StatusCode.ShouldBe(HttpStatusCode.Conflict);

            var moveResponse = await _client.PatchAsJsonAsync($"/api/cards/{cardId}", new
            {
                boardColumnId = columnId,
                concurrencyToken
            });
            moveResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var movedJson = await moveResponse.Content.ReadFromJsonAsync<JsonElement>();
            movedJson.GetProperty("id").GetGuid().ShouldBe(cardId);
            movedJson.GetProperty("boardColumnId").GetGuid().ShouldBe(columnId);
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for test temp directory.
            }
        }
    }
}
