using System.Net;
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
}
