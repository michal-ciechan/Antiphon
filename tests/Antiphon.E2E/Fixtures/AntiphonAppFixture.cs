using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Antiphon.Server.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace Antiphon.E2E.Fixtures;

/// <summary>
/// WebApplicationFactory-based fixture for E2E tests.
/// Provides a real ASP.NET Core host backed by a PostgreSQL testcontainer.
/// Supports UsePrebuiltFrontend flag to toggle between serving static files
/// (from client/dist) or expecting a Vite dev server to be running.
/// </summary>
public class AntiphonAppFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("antiphon_e2e")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;

    /// <summary>
    /// When true, the app serves prebuilt React assets from wwwroot (client/dist).
    /// When false, E2E tests should point the browser at the Vite dev server
    /// which proxies API calls to this host.
    /// </summary>
    public bool UsePrebuiltFrontend { get; set; }

    /// <summary>
    /// The base address of the test server.
    /// </summary>
    public string BaseAddress => _factory.Server.BaseAddress.ToString().TrimEnd('/');

    public HttpClient HttpClient { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                if (UsePrebuiltFrontend)
                {
                    builder.UseWebRoot(Path.Combine(FindClientDistPath(), "dist"));
                }

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

        HttpClient = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        HttpClient.Dispose();
        await _factory.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Creates a new HttpClient for the test server (useful for parallel requests).
    /// </summary>
    public HttpClient CreateClient() => _factory.CreateClient();

    /// <summary>
    /// Provides access to the DI container for resolving services in tests.
    /// </summary>
    public IServiceProvider Services => _factory.Services;

    private static string FindClientDistPath()
    {
        // Walk up from the test assembly location to find the client directory
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var clientPath = Path.Combine(dir, "client");
            if (Directory.Exists(clientPath))
            {
                return clientPath;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException(
            "Could not find client/ directory. Ensure the frontend has been built with 'npm run build'.");
    }
}
