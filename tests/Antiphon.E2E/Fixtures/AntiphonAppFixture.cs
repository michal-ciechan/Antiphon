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
using Antiphon.SessionRunner.Contracts;
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
    private IsolatedSessionRunner? _isolatedSessionRunner;

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

    /// <summary>
    /// True when <c>GET {SessionRunner:BaseUrl}/sessions</c> returned 2xx at fixture startup.
    /// </summary>
    public bool SessionRunnerReachable { get; private set; }

    /// <summary>
    /// Probe text (URL + status or exception) written to notes.log and thrown by
    /// <see cref="EnsureSessionRunnerReachable"/>.
    /// </summary>
    public string SessionRunnerReachabilityVerdict { get; private set; } =
        "[session-runner] reachability was not recorded";

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

        _isolatedSessionRunner = new IsolatedSessionRunner(GetRandomAvailablePort, FindRepositoryRoot());
        var runnerStartup = _isolatedSessionRunner.StartAsync();
        var containerStartup = _container.StartAsync();
        try
        {
            await Task.WhenAll(runnerStartup, containerStartup);
        }
        catch
        {
            await _isolatedSessionRunner.DisposeAsync();
            throw;
        }

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
            DiagnosticsDirectory,
            _isolatedSessionRunner.BaseUrl
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
    /// Fail immediately with the startup probe verdict when <c>/sessions</c> was not 2xx.
    /// Session-dependent tests call this instead of burning a 30–60s Running-status timeout.
    /// The runner is an isolated child process owned by this fixture.
    /// </summary>
    public void EnsureSessionRunnerReachable()
    {
        if (SessionRunnerReachable)
            return;

        throw new InvalidOperationException(SessionRunnerReachabilityVerdict);
    }

    /// <summary>
    /// Records whether the configured session runner is actually there.
    ///
    /// The fixture starts an isolated child runner before building the server host. The verdict is
    /// stored so <see cref="EnsureSessionRunnerReachable"/> can fail session-dependent tests in
    /// seconds with the child's URL and status or exception.
    /// </summary>
    private async Task RecordSessionRunnerReachabilityAsync()
    {
        var baseUrl = _kestrelHost?.Services
            .GetRequiredService<IConfiguration>()["SessionRunner:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            SessionRunnerReachable = false;
            SessionRunnerReachabilityVerdict = "[session-runner] no SessionRunner:BaseUrl configured";
            _diagnostics?.Note(SessionRunnerReachabilityVerdict);
            return;
        }

        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            var response = await probe.GetAsync($"{baseUrl.TrimEnd('/')}/sessions");
            if (response.IsSuccessStatusCode)
            {
                SessionRunnerReachable = true;
                SessionRunnerReachabilityVerdict =
                    $"[session-runner] our isolated child at {baseUrl} responding ({(int)response.StatusCode})";
            }
            else
            {
                SessionRunnerReachable = false;
                SessionRunnerReachabilityVerdict =
                    $"[session-runner] our isolated child at {baseUrl} answered {(int)response.StatusCode} for /sessions. "
                    + "Tests needing a live agent session will fail. Captured runner output tail: "
                    + GetIsolatedRunnerOutputTail();
            }

            _diagnostics?.Note(SessionRunnerReachabilityVerdict);
        }
        catch (Exception ex)
        {
            SessionRunnerReachable = false;
            SessionRunnerReachabilityVerdict =
                $"[session-runner] our isolated child at {baseUrl} unreachable ({ex.GetBaseException().Message}). "
                + "Tests needing a live agent session will fail. Captured runner output tail: "
                + GetIsolatedRunnerOutputTail();
            _diagnostics?.Note(SessionRunnerReachabilityVerdict);
        }
    }

    public async Task DisposeAsync()
    {
        // CARD-0102: stop what this fixture started, BEFORE the database and the host it needs to
        // identify those sessions are torn down. Sets SessionLeakVerdict rather than throwing here —
        // a leak must fail the run, and it must not do so by abandoning a Postgres container.
        await StopSessionsThisFixtureStartedAsync();

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

        // Last, so everything above has already been released. A leaked pty-host is a red test in
        // the run that caused it — which is the whole point of P2-2 item 1.
        if (SessionLeakVerdict is not null)
            throw new InvalidOperationException(SessionLeakVerdict);
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
        var clientPath = Path.Combine(FindRepositoryRoot(), "client");
        EnsureClientBundleIsCurrent(clientPath);
        return clientPath;
    }

    internal static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, "client")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException(
            "Could not find client/ directory. Ensure the frontend has been built with 'npm run build'.");
    }

    private string GetIsolatedRunnerOutputTail() =>
        _isolatedSessionRunner?.GetOutputTail() ?? "(isolated runner was not started)";

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
    /// How long a pty-host this fixture started may take to die after its session is killed before
    /// the run is failed. Generous: the kill is a frame to a detached process which then shuts its
    /// own host down, and a slow machine must not turn a clean teardown into a red run.
    /// </summary>
    private static readonly TimeSpan HostShutdownBudget = TimeSpan.FromSeconds(20);

    /// <summary>The detached pty-host, by process name — the pid-reuse guard for the census.</summary>
    private const string PtyHostProcessName = "Antiphon.PtyHost";

    /// <summary>
    /// Set when teardown found a pty-host this fixture started still alive. Thrown at the very END
    /// of <see cref="DisposeAsync"/>, never before: a leak must fail the run, and it must not do so
    /// by abandoning the Postgres container and the Kestrel host on the way out.
    /// </summary>
    public string? SessionLeakVerdict { get; private set; }

    /// <summary>
    /// CARD-0102, and item 1 of the coverage plan's P2-2: snapshot every session on this
    /// fixture-owned runner, use its bulk kill endpoint, then FAIL THE RUN if any named pty-host
    /// is still alive.
    ///
    /// <para>The fixture owns a dedicated runner, so its complete session list cannot contain an
    /// operator or delegate session. Database rows remain a diagnostic cross-check only: a runner
    /// session with no row is still ours and must be killed and censused.</para>
    ///
    /// <para><b>Ownership is now structural.</b> The fixture's private runner is configured with a
    /// per-run manifest root, so it cannot share pty-host state with the production daemon.</para>
    ///
    /// <para>Note this is why shortening <c>PtyHostLingerHours</c> — CARD-0102's own suggested
    /// remedy — would not have helped: <c>HostSession</c> starts the linger clock only after the
    /// child exits, and an interactive <c>cmd.exe</c> never does. Those hosts were not lingering
    /// orphans, they were live sessions nobody stopped. The fix is lifecycle, not TTL.</para>
    ///
    /// <para>An unreachable runner at teardown is NOT a leak verdict. It is the absence of evidence,
    /// and a suite that went red because a daemon was restarting would be worse than one that says
    /// nothing.</para>
    /// </summary>
    private async Task StopSessionsThisFixtureStartedAsync()
    {
        var runner = _isolatedSessionRunner;
        if (runner is null)
            return;

        var censusStarted = false;
        var censusCompleted = false;
        try
        {
            var listed = await IsolatedSessionRunnerTeardown.SnapshotKillAllThenCensusAsync(
                runner,
                RecordSessionDatabaseCrossCheckAsync,
                async hosts =>
                {
                    censusStarted = true;
                    await AssertNoLeakedPtyHostsAsync(hosts);
                    censusCompleted = true;
                },
                ex => _diagnostics?.Note($"[session-cleanup] kill-all failed: {ex.Message}"),
                CancellationToken.None);

            _diagnostics?.Note(
                $"[session-cleanup] issued kill-all for {listed.Count} session(s) on this fixture's runner.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _diagnostics?.Note(
                $"[session-cleanup] our isolated runner was unreachable at teardown ({ex.Message}); "
                + "its sessions could not be verified. This is not a leak verdict.");
        }
        finally
        {
            // A runner tree-kill cannot reach detached hosts. It is safe only after the census
            // above, and is bounded inside IsolatedSessionRunner so teardown cannot hang forever.
            await runner.StopAsync();
            if ((!censusStarted || censusCompleted) && SessionLeakVerdict is null)
                await runner.DeleteRunDirectoryBestEffortAsync();
        }
    }

    private async Task RecordSessionDatabaseCrossCheckAsync(IReadOnlyList<RunnerSessionDto> listed)
    {
        if (_kestrelHost is null)
            return;

        try
        {
            using var scope = _kestrelHost.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var databaseSessionIds = await db.AgentSessions.Select(s => s.Id).ToListAsync();
            var missingRows = listed.Count(s => !databaseSessionIds.Contains(s.SessionId));
            _diagnostics?.Note(
                $"[session-cleanup] runner snapshot contained {listed.Count} session(s); the fixture database "
                + $"contained {databaseSessionIds.Count} row(s), with {missingRows} runner session(s) lacking a row.");
        }
        catch (Exception ex)
        {
            _diagnostics?.Note(
                $"[session-cleanup] could not cross-check the runner snapshot against this fixture's database: {ex.Message}");
        }
    }

    /// <summary>
    /// The census: every pty-host this fixture started must be gone. A leak becomes a red test in
    /// the run that caused it, rather than a process nobody notices for a day.
    ///
    /// <para>The process NAME is checked as well as the pid, because pids are reused: on a machine
    /// that has cycled through them, a bare "is this pid alive" would fail the run over some
    /// unrelated program that happened to inherit the number.</para>
    /// </summary>
    private async Task AssertNoLeakedPtyHostsAsync(IReadOnlyList<SessionHostSnapshot> hosts)
    {
        if (hosts.Count == 0)
            return;

        var deadline = DateTime.UtcNow + HostShutdownBudget;
        List<SessionHostSnapshot> alive;
        while (true)
        {
            alive = [.. hosts.Where(h => IsPtyHostAlive(h.HostPid))];
            if (alive.Count == 0 || DateTime.UtcNow >= deadline)
                break;
            await Task.Delay(500);
        }

        if (alive.Count == 0)
        {
            _diagnostics?.Note($"[session-cleanup] all {hosts.Count} pty-host(s) are gone.");
            return;
        }

        SessionLeakVerdict =
            $"CARD-0102: this E2E fixture leaked {alive.Count} of {hosts.Count} pty-host(s) in its "
            + $"isolated session-runner run directory. Still alive after {HostShutdownBudget.TotalSeconds:0}s: "
            + string.Join(", ", alive.Select(h => $"pid {h.HostPid} (session {h.SessionId})"))
            + ". Do not 'fix' this by widening the budget — find what started a session this "
            + "teardown could not stop.";
        _diagnostics?.Note("[session-cleanup] " + SessionLeakVerdict);
    }

    private static bool IsPtyHostAlive(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited
                && string.Equals(process.ProcessName, PtyHostProcessName, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false; // no such process — the good outcome
        }
        catch (InvalidOperationException)
        {
            return false; // exited between lookup and inspection
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
        private readonly string _sessionRunnerBaseUrl;

        public IHost? KestrelHost { get; private set; }

        public KestrelWebApplicationFactory(
            string? clientDistPath,
            string connectionString,
            int port,
            bool useMockExecutor,
            string workspacePath,
            string? diagnosticsDirectory,
            string sessionRunnerBaseUrl
        )
        {
            _clientDistPath = clientDistPath;
            _connectionString = connectionString;
            _port = port;
            _useMockExecutor = useMockExecutor;
            _workspacePath = workspacePath;
            _diagnosticsDirectory = diagnosticsDirectory;
            _sessionRunnerBaseUrl = sessionRunnerBaseUrl;
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
                    ["Agents:Definitions:e2e-raw:Exe"] = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    ["SessionRunner:BaseUrl"] = _sessionRunnerBaseUrl,
                    ["Delegation:DiagnoseEnabled"] = "false",
                    ["Delegation:DiagnoseWorkingDirectory"] = Path.Combine(_workspacePath, "diagnose"),
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
