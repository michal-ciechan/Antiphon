var builder = DistributedApplication.CreateBuilder(args);

// Stable PTY/session host. Backend and frontend can be restarted while this
// process keeps live agent sessions running.
var sessionRunner = builder
    .AddProject<Projects.Antiphon_SessionRunner>("antiphon-session-runner", options => options.ExcludeLaunchProfile = true)
    .WithHttpEndpoint(port: 17283, env: "ASPNETCORE_HTTP_PORTS");

// Backend — uses connection string from appsettings.json (CoreDev k8s PostgreSQL).
var server = builder
    .AddProject<Projects.Antiphon_Server>("server", options => options.ExcludeLaunchProfile = true)
    .WithReference(sessionRunner)
    .WaitFor(sessionRunner)
    .WithEnvironment("SessionRunner__BaseUrl", "http://localhost:17283")
    .WithHttpEndpoint(port: 17281, env: "ASPNETCORE_HTTP_PORTS");

// Frontend Vite dev server — injects services__server__http__0 so vite.config.ts
// can proxy to the server at its actual address
builder.AddNpmApp("client", "../client", "dev")
    .WithReference(server)
    .WaitFor(server)
    .WithEnvironment("BROWSER", "none")
    .WithHttpEndpoint(port: 17282, env: "VITE_PORT");

builder.Build().Run();
