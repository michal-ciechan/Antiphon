using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Agents;
using Antiphon.Server.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace Antiphon.E2E.Fixtures;

/// <summary>
/// Fixture for E2E tests backed by a PostgreSQL testcontainer.
/// Starts the real Antiphon app on a random TCP port via Kestrel
/// so both HttpClient and Playwright can connect to it.
/// </summary>
public class AntiphonAppFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("antiphon_e2e")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private IHost? _kestrelHost;
    private WebApplicationFactory<Program>? _factory;

    /// <summary>
    /// When true, the app serves prebuilt React assets from wwwroot (client/dist).
    /// </summary>
    public bool UsePrebuiltFrontend { get; set; }

    /// <summary>
    /// When true, replaces AgentExecutor with MockExecutor so stages complete
    /// immediately without requiring real LLM credentials. Use in tests that
    /// need completed stages/artifacts.
    /// </summary>
    public bool UseMockExecutor { get; set; }

    /// <summary>
    /// The real TCP address that both HttpClient and Playwright can use.
    /// </summary>
    public string BaseAddress { get; private set; } = null!;

    /// <summary>
    /// Alias for BaseAddress — a real TCP endpoint Playwright Chromium can navigate to.
    /// </summary>
    public string PlaywrightAddress => BaseAddress;

    public HttpClient HttpClient { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var connectionString = _container.GetConnectionString();
        var port = GetRandomAvailablePort();

        _factory = new KestrelWebApplicationFactory(
            UsePrebuiltFrontend ? FindClientDistPath() : null,
            connectionString,
            port,
            UseMockExecutor
        );

        // Trigger host creation (WAF builds host on first access)
        // CreateClient() accesses the dummy TestServer host; we ignore its result.
        try
        {
            _factory.CreateClient();
        }
        catch
        {
            // The dummy host has no real endpoints — that's fine.
        }

        var kestrelFactory = (KestrelWebApplicationFactory)_factory;
        _kestrelHost = kestrelFactory.KestrelHost
            ?? throw new InvalidOperationException("Kestrel host was not started.");

        BaseAddress = $"http://127.0.0.1:{port}";
        HttpClient = new HttpClient { BaseAddress = new Uri(BaseAddress) };
    }

    public async Task DisposeAsync()
    {
        HttpClient.Dispose();

        if (_kestrelHost is not null)
        {
            await _kestrelHost.StopAsync();
            _kestrelHost.Dispose();
        }

        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    /// <summary>
    /// Creates a new HttpClient pointed at the real Kestrel endpoint.
    /// </summary>
    public HttpClient CreateClient() => new() { BaseAddress = new Uri(BaseAddress) };

    /// <summary>
    /// Provides access to the DI container from the Kestrel host.
    /// </summary>
    public IServiceProvider Services => _kestrelHost?.Services
        ?? throw new InvalidOperationException("Host not initialized.");

    private static string FindClientDistPath()
    {
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

    private static int GetRandomAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Custom WebApplicationFactory that starts the real app on Kestrel
    /// instead of TestServer. WAF applies all ConfigureWebHost overrides
    /// (DB replacement etc.) to the host builder. In CreateHost, we override
    /// TestServer with Kestrel so the app listens on a real TCP port.
    /// A dummy TestServer host is returned to satisfy WAF internals.
    /// </summary>
    private sealed class KestrelWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string? _clientDistPath;
        private readonly string _connectionString;
        private readonly int _port;
        private readonly bool _useMockExecutor;

        public IHost? KestrelHost { get; private set; }

        public KestrelWebApplicationFactory(
            string? clientDistPath,
            string connectionString,
            int port,
            bool useMockExecutor = false
        )
        {
            _clientDistPath = clientDistPath;
            _connectionString = connectionString;
            _port = port;
            _useMockExecutor = useMockExecutor;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            if (_clientDistPath is not null)
            {
                builder.UseWebRoot(Path.Combine(_clientDistPath, "dist"));
            }

            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                );
                if (descriptor is not null)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<AppDbContext>(options =>
                    options.UseNpgsql(_connectionString, npgsql =>
                    {
                        npgsql.MigrationsAssembly("Antiphon.Server");
                        npgsql.SetPostgresVersion(16, 0);
                    })
                );

                if (_useMockExecutor)
                {
                    // Remove the real IStageExecutor and replace with MockExecutor
                    var executorDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(IStageExecutor)
                    );
                    if (executorDescriptor is not null)
                        services.Remove(executorDescriptor);

                    services.AddScoped<IStageExecutor, MockExecutor>();
                }
            });
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            // WAF has already added UseTestServer() to the builder.
            // Override it with Kestrel so the app listens on a real TCP port.
            // ConfigureWebHost callbacks run in order; ours runs AFTER WAF's,
            // so UseKestrel() replaces TestServer.
            builder.ConfigureWebHost(wb =>
            {
                wb.UseKestrel();
                wb.UseUrls($"http://127.0.0.1:{_port}");
            });

            KestrelHost = builder.Build();
            KestrelHost.Start();

            // WAF calls GetTestServer() on the returned host.
            // Return a dummy host with TestServer to satisfy that.
            var dummyBuilder = new HostBuilder();
            dummyBuilder.ConfigureWebHost(wb =>
            {
                wb.UseTestServer();
                wb.Configure(app => { });
            });
            var dummyHost = dummyBuilder.Build();
            dummyHost.Start();

            return dummyHost;
        }

        protected override void Dispose(bool disposing)
        {
            // Don't dispose KestrelHost here — the outer fixture handles it
            base.Dispose(disposing);
        }
    }
}
