using Antiphon.AppHost.Supervisor;

var builder = DistributedApplication.CreateBuilder(args);

// Repo root: AppHost is at <repo>/Antiphon.AppHost/, binary at bin/Debug/net9.0/
var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

// ── Supervisor infrastructure ─────────────────────────────────────────────────
builder.AddDaemonSupervisor();

// ── PostgreSQL (Aspire-managed container, persistent named volume) ────────────
// Fixed password so the persistent volume survives AppHost restarts.
var pgPassword = builder.AddParameter("pg-password", "antiphon_dev", secret: true);

var postgres = builder.AddPostgres("DefaultConnection", password: pgPassword)
    .WithImageTag("16")
    .WithDataVolume("antiphon-pgdata");

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
    .WaitFor(postgres)
    .WithEnvironment("SessionRunner__BaseUrl", "http://localhost:17204")
    .WithHttpEndpoint(port: 17202, env: "ASPNETCORE_HTTP_PORTS");

// ── React / Vite client ───────────────────────────────────────────────────────
builder.AddNpmApp("client", "../client", "dev")
    .WithReference(server)
    .WaitFor(server)
    .WithEnvironment("BROWSER", "none")
    .WithHttpEndpoint(port: 17203, env: "VITE_PORT");

builder.Build().Run();
