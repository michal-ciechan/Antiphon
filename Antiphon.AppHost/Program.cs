var builder = DistributedApplication.CreateBuilder(args);

// Backend — uses connection string from appsettings.json (CoreDev k8s PostgreSQL).
var server = builder.AddProject<Projects.Antiphon_Server>("server");

// Frontend Vite dev server — injects services__server__http__0 so vite.config.ts
// can proxy to the server at its actual address
builder.AddNpmApp("client", "../client", "dev")
    .WithReference(server)
    .WaitFor(server)
    .WithEnvironment("BROWSER", "none")
    .WithHttpEndpoint(port: 5173, env: "VITE_PORT");

builder.Build().Run();
