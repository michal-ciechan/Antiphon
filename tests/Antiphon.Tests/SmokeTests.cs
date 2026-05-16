using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Microsoft.AspNetCore.Mvc.Testing;
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
}
