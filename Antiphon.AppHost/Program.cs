using Antiphon.AppHost.Supervisor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

// ── Messaging broker (CARD-0185) ──────────────────────────────────────────────────────────
// Default: whatever server/appsettings.json says — localhost:19092, the docker-compose.dev.yml
// Redpanda that the fake gateway (:17208) also uses. A LIVE broker (this machine: am-redpanda on
// server2 over Tailscale, which the real Family Telegram gateway produces to) is a per-machine
// opt-in that never appears in source:
//   dotnet user-secrets set "AntiphonMessaging:BootstrapServers" "server2:19092" --project Antiphon.AppHost
// or the gitignored Antiphon.AppHost/appsettings.Development.json. Forwarded verbatim as the
// server's AntiphonMessaging__BootstrapServers; the fake gateway is deliberately NOT forwarded
// (a fake inbound on the live broker would be answered through the real bot), so while live,
// POST :17208/inbound does not reach the server. It is one broker or the other.
var liveBroker = builder.Configuration["AntiphonMessaging:BootstrapServers"];

// ── .NET API server ───────────────────────────────────────────────────────────
var server = builder
    .AddProject<Projects.Antiphon_Server>("server", options => options.ExcludeLaunchProfile = true)
    .WithReference(postgres)
    .WithEnvironment("SessionRunner__BaseUrl", "http://localhost:17204")
    // Pin Development: with ASPNETCORE_ENVIRONMENT unset, ASP.NET Core defaults to Production and
    // would load appsettings.Production.json.
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ChannelBridge__Enabled", "true")
    // The modern pseudoconsole, ON for this deployment (CARD-0037 step 3). The session-runner gets
    // it from its own appsettings (SessionRunner:PtyBackend) and its detached pty-hosts inherit
    // that; this is the server's half — its in-proc pty adapters and, through PtyDeliveryProfile,
    // the delivery ceilings every typed body is sized against. The two are resolved independently
    // and PtyDeliveryProfile verifies they agree before it uses the raised ceilings, so a machine
    // where the redistributable is missing falls back to the inbox conhost with the old ceilings
    // rather than typing 43 KB into a pty that clips at 1 KB.
    .WithEnvironment("ANTIPHON_PTY_BACKEND", "modern")
    .WithHttpEndpoint(port: 17202, env: "ASPNETCORE_HTTP_PORTS");

if (!string.IsNullOrWhiteSpace(liveBroker))
    server.WithEnvironment("AntiphonMessaging__BootstrapServers", liveBroker.Trim());

// ── React / Vite client ───────────────────────────────────────────────────────
// "serve" runs client/scripts/serve.mjs (CARD-0216), a self-supervising shim that reads
// logs/client.mode (default "built") and runs either a built bundle behind `vite preview` or a
// plain `vite` dev server on this same port, swapping between them without an AppHost restart
// when scripts/client-mode.ps1 changes the mode file. See AGENTS.md, "Running Locally".
builder.AddNpmApp("client", "../client", "serve")
    .WithReference(server)
    .WaitFor(server)
    .WithEnvironment("BROWSER", "none")
    .WithHttpEndpoint(port: 17203, env: "VITE_PORT");

// ── Storybook (component workshop — same client project, "storybook" npm script on :17209) ──
// Started so its Caddy vhost (storybook.antiphon.<machine>.codeperf.net) has something to proxy.
// isProxied:false because the script pins -p 17209; Aspire just tracks that direct endpoint.
builder.AddNpmApp("storybook", "../client", "storybook")
    .WithEnvironment("BROWSER", "none")
    .WithHttpEndpoint(port: 17209, isProxied: false);

var app = builder.Build();
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Antiphon.AppHost");

// Report which mode client/scripts/serve.mjs will find on its first poll. This is a startup log
// line only - the shim re-reads the file itself every ~1s and is the actual source of truth, so
// this can go stale the instant someone runs scripts/client-mode.ps1 without restarting.
var clientModeFile = Path.Combine(repoRoot, "logs", "client.mode");
var clientMode = File.Exists(clientModeFile) ? File.ReadAllText(clientModeFile).Trim() : "";
logger.LogInformation(
    "Client (port 17203) starting in {Mode} mode ({Source})",
    string.IsNullOrWhiteSpace(clientMode) ? "built" : clientMode,
    string.IsNullOrWhiteSpace(clientMode) ? "default - no logs/client.mode yet" : "logs/client.mode");

if (!string.IsNullOrWhiteSpace(liveBroker))
{
    logger.LogInformation(
        "Messaging broker for server: {Broker} ({Source}); fake-gateway stays on localhost:19092 and will not reach the server while the live broker is selected",
        liveBroker.Trim(),
        "AppHost configuration (per-machine opt-in)");
}
else
{
    logger.LogInformation(
        "Messaging broker for server: {Broker} ({Source})",
        "localhost:19092",
        "default (server/appsettings.json)");
}

app.Run();
