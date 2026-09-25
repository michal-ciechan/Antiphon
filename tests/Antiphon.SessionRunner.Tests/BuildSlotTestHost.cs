using Antiphon.SessionRunner;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0589: a loopback Kestrel host carrying only the runner's build-slot registration and routes
/// (<see cref="BuildSlotRoutes"/>, the same code <c>Program.cs</c> maps), on a random port. It never
/// binds the production runner's 17204 or 8080, so a wrapper pointed at it through
/// <c>ANTIPHON_BUILD_SLOTS_URL</c> cannot touch a real host budget.
/// </summary>
internal sealed class BuildSlotTestHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private BuildSlotTestHost(WebApplication app, Uri address)
    {
        _app = app;
        Address = address;
        Http = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(20) };
    }

    public Uri Address { get; }
    public HttpClient Http { get; }
    public string SlotsUrl => new Uri(Address, "build-slots").ToString();
    public BuildSlotBroker Broker => _app.Services.GetRequiredService<BuildSlotBroker>();

    public static async Task<BuildSlotTestHost> StartAsync(
        Dictionary<string, string?> settings,
        IProcessLivenessProbe? liveness = null,
        IHostMemoryProbe? memory = null,
        TimeProvider? time = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Test" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(settings);
        if (liveness is not null) builder.Services.AddSingleton(liveness);
        if (memory is not null) builder.Services.AddSingleton(memory);
        if (time is not null) builder.Services.AddSingleton(time);
        builder.Services.AddBuildSlotBroker(builder.Configuration);

        var app = builder.Build();
        app.MapBuildSlotRoutes();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>()
            .Addresses.Single();
        var uri = new Uri(address.TrimEnd('/') + "/");
        if (uri.Port is 17204 or 8080)
            throw new InvalidOperationException($"build-slot test host bound a production runner port: {uri}");
        return new BuildSlotTestHost(app, uri);
    }

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
