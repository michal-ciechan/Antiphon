using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Antiphon.AppHost.Supervisor;

/// <summary>
/// Runs a minimal HTTP API on port 17289 for scripted start/stop/restart.
/// Usage: POST http://localhost:17289/control/{name}/start|stop|restart
/// </summary>
internal sealed class ControlApiService(DaemonProcessService supervisor) : BackgroundService
{
    private const int Port = 17289;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var app = WebApplication.Create();
        app.Urls.Add($"http://localhost:{Port}");

        app.MapPost("/control/{name}/start", async (string name) =>
        {
            await supervisor.StartAsync(name, stoppingToken);
            return Results.Ok(new { name, action = "started" });
        });

        app.MapPost("/control/{name}/stop", async (string name) =>
        {
            await supervisor.StopAsync(name, stoppingToken);
            return Results.Ok(new { name, action = "stopped" });
        });

        app.MapPost("/control/{name}/restart", async (string name) =>
        {
            await supervisor.RestartAsync(name, stoppingToken);
            return Results.Ok(new { name, action = "restarted" });
        });

        app.MapGet("/control/status", () =>
            Results.Ok(new { message = "Antiphon control API", port = Port }));

        await app.RunAsync(stoppingToken);
    }
}
