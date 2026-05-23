using Antiphon.AppHost.Supervisor;

var builder = DistributedApplication.CreateBuilder(args);

// Repo root: AppHost is at <repo>/Antiphon.AppHost/, binary at bin/Debug/net9.0/
var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

// ── Supervisor infrastructure ─────────────────────────────────────────────────
builder.AddDaemonSupervisor();

// ── PostgreSQL (Aspire-managed container, persistent named volume) ────────────
var postgres = builder.AddPostgres("postgres")
    .WithEndpoint(port: 17280, targetPort: 5432, name: "tcp")
    .WithDataVolume("antiphon-pgdata");

// ── Session runner (daemon — survives AppHost exit, keeps live PTY sessions alive)
builder.AddDaemonProcess("session-runner", new DaemonProcessConfig(
    Executable:       "dotnet",
    Args:             ["run", "--urls", "http://localhost:17283"],
    WorkingDirectory: Path.Combine(repoRoot, "src", "Antiphon.SessionRunner"),
    Port:             17283,
    HealthPath:       "/health"));

// ── .NET API server ───────────────────────────────────────────────────────────
var server = builder
    .AddProject<Projects.Antiphon_Server>("server", options => options.ExcludeLaunchProfile = true)
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithEnvironment("SessionRunner__BaseUrl", "http://localhost:17283")
    .WithHttpEndpoint(port: 17281, env: "ASPNETCORE_HTTP_PORTS");

// ── React / Vite client ───────────────────────────────────────────────────────
builder.AddNpmApp("client", "../client", "dev")
    .WithReference(server)
    .WaitFor(server)
    .WithEnvironment("BROWSER", "none")
    .WithHttpEndpoint(port: 17282, env: "VITE_PORT");

builder.Build().Run();
