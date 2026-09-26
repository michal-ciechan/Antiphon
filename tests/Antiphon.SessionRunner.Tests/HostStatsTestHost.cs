using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0718 MS-4. Loopback host for the host-stats routes only. It never binds 17204 or 8080.
/// The sampler is registered but not started; tests call <see cref="HostStatsSamplerService.SampleOnceAsync"/>.
/// </summary>
internal sealed class HostStatsTestHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private HostStatsTestHost(WebApplication app, Uri address)
    {
        _app = app;
        Address = address;
        Http = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(20) };
    }

    public Uri Address { get; }
    public HttpClient Http { get; }
    public HostStatsSamplerService Sampler => _app.Services.GetRequiredService<HostStatsSamplerService>();

    public static async Task<HostStatsTestHost> StartAsync(
        Dictionary<string, string?> settings,
        IHostStatsProbe probe,
        TimeProvider? time = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Test" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddSingleton(probe);
        if (time is not null)
            builder.Services.AddSingleton(time);
        builder.Services.AddHostStats(builder.Configuration, startSampler: false);

        var app = builder.Build();
        app.MapHostStatsRoutes();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
        var uri = new Uri(address.TrimEnd('/') + "/");
        if (uri.Port is 17204 or 8080)
            throw new InvalidOperationException($"host-stats test host bound a production runner port: {uri}");
        return new HostStatsTestHost(app, uri);
    }

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

internal sealed class ScriptedHostStatsProbe : IHostStatsProbe
{
    public double Cpu { get; set; } = 10;
    public int Calls { get; private set; }
    public Exception? Fault { get; set; }

    public HostSample? Read()
    {
        Calls++;
        if (Fault is { } fault)
        {
            Fault = null;
            throw fault;
        }

        return new HostSample(default, Cpu, 8, 0.5, 0.5, 0.5, 2000, 1000, 100, 50, [], 4, []);
    }
}
