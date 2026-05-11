using System.Net;
using Shouldly;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;
using Antiphon.Server.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace Antiphon.Tests;

/// <summary>
/// Basic smoke tests that verify the app starts and critical endpoints respond.
/// Uses a real PostgreSQL testcontainer via WebApplicationFactory.
/// </summary>
public class SmokeTests
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("antiphon_smoke")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    [Before(Test)]
    public async Task SetupAsync()
    {
        await _container.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Remove existing DbContext registration
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (descriptor is not null)
                    {
                        services.Remove(descriptor);
                    }

                    // Add testcontainer PostgreSQL
                    services.AddDbContext<AppDbContext>(options =>
                        options.UseNpgsql(_container.GetConnectionString(), npgsql =>
                        {
                            npgsql.MigrationsAssembly("Antiphon.Server");
                            npgsql.SetPostgresVersion(16, 0);
                        }));
                });
            });

        _client = _factory.CreateClient();
    }

    [After(Test)]
    public async Task TeardownAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _container.DisposeAsync();
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
        // If we get here without exception, the app started successfully
        // with testcontainer DB and all middleware registered.
        var response = await _client.GetAsync("/health");

        response.StatusCode.ShouldNotBe(HttpStatusCode.InternalServerError);
    }
}
