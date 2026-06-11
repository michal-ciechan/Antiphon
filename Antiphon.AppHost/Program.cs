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
builder.AddDaemonProcess("session-runner", new DaemonProcessConfig(
    Executable:       "dotnet",
    Args:             ["run", "--urls", "http://localhost:17204"],
    WorkingDirectory: Path.Combine(repoRoot, "src", "Antiphon.SessionRunner"),
    Port:             17204,
    HealthPath:       "/health"));

// ── .NET API server ───────────────────────────────────────────────────────────
var server = builder
    .AddProject<Projects.Antiphon_Server>("server", options => options.ExcludeLaunchProfile = true)
    .WithReference(postgres)
    .WithEnvironment("SessionRunner__BaseUrl", "http://localhost:17204")
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
