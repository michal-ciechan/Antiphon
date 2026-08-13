using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
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
public class AntiphonAppFixture
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("antiphon_e2e")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private IHost? _kestrelHost;
    private WebApplicationFactory<Program>? _factory;
    private string? _workspacePath;
    private TestDiagnostics? _diagnostics;

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
    /// Where the app writes its Serilog file sink. Defaults to the running test's
    /// <see cref="TestDiagnostics"/> directory, so every E2E test gets its own server log rather
    /// than one interleaved stream on stdout, and the console sink drops to Warning so a failing
    /// assertion stays visible. Set explicitly only to override the location.
    /// </summary>
    public string? DiagnosticsDirectory { get; set; }

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
        // Opt-out, not opt-in: a test that never thought about diagnostics still gets them.
        // Only adopt the running test when the caller did not choose a directory — a fixture shared
        // across tests (SharedApp) must not file its log under whichever test happened to start it.
        if (DiagnosticsDirectory is null)
        {
            _diagnostics = TestDiagnostics.ForCurrentTest();
            DiagnosticsDirectory = _diagnostics.Directory;
        }

        await _container.StartAsync();

        var connectionString = _container.GetConnectionString();
        var port = GetRandomAvailablePort();
        _workspacePath = Path.Combine(Path.GetTempPath(), "antiphon-e2e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspacePath);

        _factory = new KestrelWebApplicationFactory(
            UsePrebuiltFrontend ? FindClientDistPath() : null,
            connectionString,
            port,
            UseMockExecutor,
            _workspacePath,
            DiagnosticsDirectory
        );

        // Trigger host creation (WAF builds host on first access)
        // CreateClient() accesses the dummy TestServer host; we ignore its result.
        Exception? startupException = null;
        try
        {
            _factory.CreateClient();
        }
        catch (Exception ex)
        {
            // The dummy host has no real endpoints — that's fine.
            startupException = ex;
        }

        var kestrelFactory = (KestrelWebApplicationFactory)_factory;
        _kestrelHost = kestrelFactory.KestrelHost
            ?? throw new InvalidOperationException("Kestrel host was not started.", startupException);

        BaseAddress = $"http://127.0.0.1:{port}";
        HttpClient = CreateClient();

        await RecordSessionRunnerReachabilityAsync();
    }

    /// <summary>
    /// Records whether the configured session runner is actually there.
    ///
    /// This fixture does NOT start one — it uses the <c>SessionRunner:BaseUrl</c> from
    /// appsettings.json and talks to whatever happens to be listening. When nothing is (or when an
    /// unrelated process has taken the port — a stray node dev server was squatting it, answering
    /// 404), every test that needs a live session fails 30-60s later with "did not reach Running
    /// status" and no hint as to why. One line up front turns that into an obvious answer.
    /// </summary>
    private async Task RecordSessionRunnerReachabilityAsync()
    {
        if (_diagnostics is null || _kestrelHost is null)
            return;

        var baseUrl = _kestrelHost.Services
            .GetRequiredService<IConfiguration>()["SessionRunner:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _diagnostics.Note("[session-runner] no SessionRunner:BaseUrl configured");
            return;
        }

        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            var response = await probe.GetAsync($"{baseUrl.TrimEnd('/')}/sessions");
            _diagnostics.Note(response.IsSuccessStatusCode
                ? $"[session-runner] {baseUrl} responding ({(int)response.StatusCode})"
                : $"[session-runner] {baseUrl} answered {(int)response.StatusCode} for /sessions — "
                    + "something is on that port but it is not a session runner. "
                    + "Tests needing a live agent session will fail.");
        }
        catch (Exception ex)
        {
            _diagnostics.Note(
                $"[session-runner] {baseUrl} unreachable ({ex.GetBaseException().Message}). "
                + "Tests needing a live agent session will fail.");
        }
    }

    public async Task DisposeAsync()
    {
        // Covers the API-only tests, which have no page and so never called
        // CaptureOnCompletionAsync; a no-op when they did. Skipped for a fixture with a
        // caller-chosen directory, which is not tied to one test's pass/fail.
        if (_diagnostics is not null)
            await _diagnostics.CompleteFromContextAsync();

        HttpClient?.Dispose();

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

        if (_workspacePath is not null && Directory.Exists(_workspacePath))
        {
            await DeleteDirectoryBestEffortAsync(_workspacePath);
        }
    }

    /// <summary>
    /// Creates a new HttpClient pointed at the real Kestrel endpoint.
    /// </summary>
    public HttpClient CreateClient() => new(new SocketsHttpHandler { UseProxy = false })
    {
        BaseAddress = new Uri(BaseAddress)
    };

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
                EnsureClientBundleIsCurrent(clientPath);
                return clientPath;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException(
            "Could not find client/ directory. Ensure the frontend has been built with 'npm run build'.");
    }

    /// <summary>
    /// Refuses to run browser tests against a stale bundle.
    ///
    /// These tests serve <c>client/dist</c>, which is a build artefact nothing rebuilds
    /// automatically — so a browser test silently asserted against whatever frontend was last
    /// built. A month-old bundle made new UI tests fail for no visible reason, and, far worse,
    /// would let a test of REMOVED UI keep passing. Failing loudly with the offending file is the
    /// only honest option: a green browser suite has to mean the current frontend is green.
    /// </summary>
    private static void EnsureClientBundleIsCurrent(string clientPath)
    {
        var indexHtml = Path.Combine(clientPath, "dist", "index.html");
        if (!File.Exists(indexHtml))
        {
            throw new InvalidOperationException(
                $"client/dist has not been built ({indexHtml} is missing). Run 'npm run build' in client/ "
                + "before the E2E suite — browser tests serve this bundle.");
        }

        var builtAt = File.GetLastWriteTimeUtc(indexHtml);
        var sourceRoot = Path.Combine(clientPath, "src");
        if (!Directory.Exists(sourceRoot))
            return;

        var newer = Directory
            .EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".test.ts", StringComparison.OrdinalIgnoreCase)
                && !f.EndsWith(".test.tsx", StringComparison.OrdinalIgnoreCase)
                && !f.EndsWith(".stories.tsx", StringComparison.OrdinalIgnoreCase))
            .Select(f => new { Path = f, Modified = File.GetLastWriteTimeUtc(f) })
            .Where(f => f.Modified > builtAt)
            .OrderByDescending(f => f.Modified)
            .FirstOrDefault();

        if (newer is not null)
        {
            throw new InvalidOperationException(
                $"client/dist is stale: built {builtAt:u}, but {Path.GetRelativePath(clientPath, newer.Path)} "
                + $"changed {newer.Modified:u}. Run 'npm run build' in client/ — otherwise these browser "
                + "tests assert against an old frontend and their result means nothing.");
        }
    }

    private static int GetRandomAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
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
        private readonly string _workspacePath;
        private readonly string? _diagnosticsDirectory;

        public IHost? KestrelHost { get; private set; }

        public KestrelWebApplicationFactory(
            string? clientDistPath,
            string connectionString,
            int port,
            bool useMockExecutor,
            string workspacePath,
            string? diagnosticsDirectory
        )
        {
            _clientDistPath = clientDistPath;
            _connectionString = connectionString;
            _port = port;
            _useMockExecutor = useMockExecutor;
            _workspacePath = workspacePath;
            _diagnosticsDirectory = diagnosticsDirectory;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            if (_clientDistPath is not null)
            {
                builder.UseWebRoot(Path.Combine(_clientDistPath, "dist"));
            }

            builder.ConfigureAppConfiguration((_, config) =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = _connectionString,
                    ["Git:WorkspacePath"] = _workspacePath,
                    ["Git:WorktreeBasePath"] = Path.Combine(_workspacePath, "worktrees"),
                    ["GitHub:Enabled"] = "false",
                    ["Agents:DefaultDefinition"] = "e2e-raw",
                    ["Agents:Definitions:e2e-raw:Kind"] = "Raw",
                    ["Agents:Definitions:e2e-raw:Exe"] = Path.Combine(Environment.SystemDirectory, "cmd.exe")
                };

                if (_diagnosticsDirectory is not null)
                {
                    // Full detail to this test's own file; only warnings and worse to stdout, so the
                    // assertion that failed is not buried under the run's own log.
                    settings["Serilog:LogPath"] = _diagnosticsDirectory;
                    settings["Serilog:ConsoleMinimumLevel"] = "Warning";
                    // EF logs every command it runs; at Information the file is ~90% SQL and the
                    // one line explaining the failure is impossible to find. Warning keeps command
                    // ERRORS (which are the useful part) and drops the successful chatter.
                    settings["Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore.Database.Command"] = "Warning";
                    settings["Serilog:MinimumLevel:Override:Microsoft.AspNetCore.Routing"] = "Warning";
                    settings["Serilog:MinimumLevel:Override:Microsoft.AspNetCore.Http.Result"] = "Warning";
                    // RE-ARMED for tests: appsettings.json turns this down to Warning because it was
                    // 26% of a 40 MB/hour production log (CARD-0043). Here the file is per-test and
                    // tiny, and the session-runner request/response trace is precisely what you read
                    // when a test fails with "did not reach Running status".
                    settings["Serilog:MinimumLevel:Override:System.Net.Http.HttpClient"] = "Information";
                }

                config.AddInMemoryCollection(settings);
            });

            builder.ConfigureServices(services =>
            {
                services.Configure<HealthCheckServiceOptions>(options =>
                {
                    options.Registrations.Clear();
                });

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
