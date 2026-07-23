using Antiphon.AppHost.Supervisor;

var builder = DistributedApplication.CreateBuilder(args);

// Repo root: AppHost is at <repo>/Antiphon.AppHost/, binary at bin/Debug/net9.0/
var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

// ── Supervisor infrastructure ─────────────────────────────────────────────────
builder.AddDaemonSupervisor();

// ── PostgreSQL (always-on, EXTERNAL container — not managed by Aspire) ─────────
// Postgres runs as a standalone docker-compose container (docker-compose.dev.yml,
// restart: unless-stopped) so it auto-starts on login and stays up whether or not
// the AppHost is running. We only reference its connection string here; the value
// comes from appsettings.json (Host=localhost;Port=17280;...). This sidesteps the
// Aspire-managed-postgres flakiness (stale DefaultConnection containers, HNS hangs).
var postgres = builder.AddConnectionString("DefaultConnection");

// ── Session runner (daemon — survives AppHost exit, keeps live PTY sessions alive)
// Launch the BUILT exe directly (not 'dotnet run'): 'dotnet run' wraps the app in a
// kill-on-close Job Object that captures the runner's detached pty-hosts and kills them on
// restart, defeating session survival. BuildProjectDir makes the supervisor rebuild before
// each launch so soft restarts still pick up new code. See the 2026-07-19 pty-host-split spec.
var sessionRunnerDir = Path.Combine(repoRoot, "src", "Antiphon.SessionRunner");
builder.AddDaemonProcess("session-runner", new DaemonProcessConfig(
    Executable:       Path.Combine(sessionRunnerDir, "bin", "Debug", "net9.0", "Antiphon.SessionRunner.exe"),
    Args:             ["--urls", "http://localhost:17204"],
    WorkingDirectory: sessionRunnerDir,
    Port:             17204,
    HealthPath:       "/health",
    BuildProjectDir:  sessionRunnerDir));

// ── FAKE messaging gateway (dev/test only — records would-be deliveries, injects inbound) ──
// The REAL Antiphon.Messaging.Service (actual Telegram egress) is deliberately NOT part of the
// dev stack (spec Q9); deployed environments run only the real gateway. Built-exe pattern for
// the same reason as the session-runner (no `dotnet run` kill-on-close job).
var fakeGatewayDir = Path.Combine(repoRoot, "src", "Antiphon.Messaging.FakeGateway");
builder.AddDaemonProcess("fake-gateway", new DaemonProcessConfig(
    Executable:       Path.Combine(fakeGatewayDir, "bin", "Debug", "net9.0", "Antiphon.Messaging.FakeGateway.exe"),
    Args:             ["--urls", "http://localhost:17208"],
    WorkingDirectory: fakeGatewayDir,
    Port:             17208,
    HealthPath:       "/health",
    BuildProjectDir:  fakeGatewayDir));

// ── .NET API server ───────────────────────────────────────────────────────────
var server = builder
    .AddProject<Projects.Antiphon_Server>("server", options => options.ExcludeLaunchProfile = true)
    .WithReference(postgres)
    .WithEnvironment("SessionRunner__BaseUrl", "http://localhost:17204")
    // Pin Development: with ASPNETCORE_ENVIRONMENT unset, ASP.NET Core defaults to Production and
    // would load appsettings.Production.json.
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ChannelBridge__Enabled", "true")
    // LIVE Telegram bridge (2026-07-23): consume the deployed am-redpanda on server2 (Tailscale)
    // that the @antiphon_assistant_bot gateway (am-service) produces to — the Family agent talks
    // to the real "Antiphon-Family" group. Comment this line out to fall back to the LOCAL broker
    // (localhost:19092) + fake gateway for offline smoke tests; it's one broker or the other, so
    // while live, POST :17208/inbound no longer reaches the server.
    .WithEnvironment("AntiphonMessaging__BootstrapServers", "server2:19092")
    .WithHttpEndpoint(port: 17202, env: "ASPNETCORE_HTTP_PORTS");

// ── React / Vite client ───────────────────────────────────────────────────────
builder.AddNpmApp("client", "../client", "dev")
    .WithReference(server)
    .WaitFor(server)
    .WithEnvironment("BROWSER", "none")
    .WithHttpEndpoint(port: 17203, env: "VITE_PORT");

// ── Storybook (component workshop — same client project, "storybook" npm script on :17283) ──
// Started so its Caddy vhost (storybook.antiphon.<machine>.codeperf.net) has something to proxy.
// isProxied:false because the script pins -p 17283; Aspire just tracks that direct endpoint.
builder.AddNpmApp("storybook", "../client", "storybook")
    .WithEnvironment("BROWSER", "none")
    .WithHttpEndpoint(port: 17283, isProxied: false);

builder.Build().Run();
