using System.Net;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Api.Middleware;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.SessionRunner.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RunnerDisposal = Antiphon.SessionRunner.HerdrPaneDisposalService;
using ServerDisposal = Antiphon.Server.Application.Services.HerdrPaneDisposalService;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Real endpoint/wire chain over random loopback ports, no Program/DB/provisioners.</summary>
internal sealed class HerdrDisposalHttpFixture : IAsyncDisposable
{
    public HerdrPaneDisposalFixture Runner { get; } = new();
    public WebApplication RunnerApp { get; private set; } = null!;
    public WebApplication ServerApp { get; private set; } = null!;
    public HttpClient Http { get; private set; } = null!;
    public bool AdvertiseCapability { get; set; } = true;
    public int RunnerDisposalRequests { get; private set; }

    public async Task StartAsync()
    {
        await Runner.StartAsync();
        var builder = NewBuilder();
        builder.Services.AddSingleton<RunnerDisposal>(_ => Runner.Service);
        RunnerApp = builder.Build();
        RunnerApp.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/herdr/pane-disposals")) RunnerDisposalRequests++;
            await next(context);
        });
        RunnerApp.MapGet("/capabilities", () => new RunnerCapabilitiesDto("ModernConPty", null, "test", false,
            SessionBackends: [SessionBackends.Herdr], Features: AdvertiseCapability ? [HerdrPaneDisposalCodes.Capability] : []));
        RunnerApp.MapHerdrPaneDisposalRoutes();
        await RunnerApp.StartAsync();

        builder = NewBuilder();
        builder.Services.AddScoped<ServerDisposal>();
        builder.Services.AddHttpClient<ISessionRunnerClient, SessionRunnerHttpClient>();
        builder.Services.AddSingleton(Options.Create(new Antiphon.Server.Application.Settings.SessionRunnerSettings
            { BaseUrl = RunnerApp.Urls.Single() }));
        ServerApp = builder.Build();
        ServerApp.UseMiddleware<ExceptionMiddleware>();
        ServerApp.MapHerdrPaneDisposalEndpoints();
        await ServerApp.StartAsync();
        Http = new() { BaseAddress = new Uri(ServerApp.Urls.Single()), Timeout = TimeSpan.FromSeconds(30) };
    }

    private static WebApplicationBuilder NewBuilder()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0));
        return builder;
    }

    public async ValueTask DisposeAsync()
    {
        Http?.Dispose();
        if (ServerApp is not null) await ServerApp.DisposeAsync();
        if (RunnerApp is not null) await RunnerApp.DisposeAsync();
        await Runner.DisposeAsync();
    }
}
