using System.Collections.Concurrent;
using Antiphon.SessionRunner.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Antiphon.SessionRunner.Tests;

/// <summary>One loopback host per test session; each spool and authority route has private state.</summary>
public sealed class C1514HostLoadFixture : IAsyncDisposable
{
    private readonly Lazy<Task<WebApplication>> _app;
    private readonly ConcurrentDictionary<string, Func<HttpContext, Task>> _authorities = new();
    private readonly SwitchingHistory _history = new();
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "c1514-runner-" + Guid.NewGuid().ToString("N"));
    public C1514HostLoadFixture() => _app = new(StartAsync);
    public HostStatsSettings Settings { get; private set; } = null!;
    public async Task<BuildSlotBroker> BrokerAsync() => (await _app.Value).Services.GetRequiredService<BuildSlotBroker>();
    public async Task<Uri> AddressAsync()
    {
        var app = await _app.Value;
        return new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single() + "/");
    }
    public async Task<string> AuthorityAsync(Func<HttpContext, Task> read)
    {
        var id = Guid.NewGuid().ToString("N"); _authorities[id] = read;
        return new Uri(await AddressAsync(), "authority/" + id).ToString();
    }
    public string PrivatePath() => Path.Combine(Root, Guid.NewGuid().ToString("N"));
    public async Task SetHistoryAsync(IHostLoadHistorySource source, bool enabled = true)
    { await AddressAsync(); _history.Source = source; Settings.HistoryEnabled = enabled; }
    private async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Test" });
        builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        { ["SessionRunner:HostStats:HistoryPath"] = Root,
            ["SessionRunner:HostStats:PhysicalHostKey"] = "owned-host", ["SessionRunner:HostStats:AuthorityId"] = "shared",
            ["SessionRunner:BuildSlots:MinAvailableMemoryMb"] = "0", ["SessionRunner:BuildSlots:MaxConcurrent"] = "6" });
        var liveness = new BuildSlotBrokerTests.FakeLiveness();
        for (var pid = 101; pid <= 104; pid++) liveness.Start(pid, T0.UtcDateTime);
        builder.Services.AddSingleton<IProcessLivenessProbe>(liveness);
        builder.Services.AddBuildSlotBroker(builder.Configuration);
        builder.Services.AddSingleton<IHostStatsProbe>(new ScriptedHostStatsProbe());
        builder.Services.AddHostStats(builder.Configuration, startSampler: false);
        builder.Services.AddSingleton<IHostMemoryProbe>(new BuildSlotBrokerTests.FakeMemory { AvailableBytes = 64L * 1024 * 1024 * 1024 });
        builder.Services.AddSingleton<IHostLoadHistorySource>(_history);
        var app = builder.Build(); app.MapHostStatsRoutes(); app.MapBuildSlotRoutes();
        app.MapGet("/authority/{id}", async (string id, HttpContext context) => await _authorities[id](context));
        await app.StartAsync();
        Settings = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<HostStatsSettings>>().Value;
        return app;
    }
    public async ValueTask DisposeAsync()
    {
        if (_app.IsValueCreated) { var app = await _app.Value; await app.StopAsync(); await app.DisposeAsync(); }
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
    }
    private sealed class SwitchingHistory : IHostLoadHistorySource
    {
        public IHostLoadHistorySource? Source { get; set; }
        public HostLoadHistoryPage ReadHistory(string? after, int limit) => Source!.ReadHistory(after, limit);
    }
    internal static HostStatsSettings Config(string path) => new()
    { HistoryPath = path, RunnerId = "owned-runner", PhysicalHostKey = "owned-host", BindingRevision = 1, SourceEpoch = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa") };
    internal static HostLoadSample Sample(HostLoadSpool spool, long sequence = 101, Guid? boot = null) => new()
    {
        Source = new("owned-runner", spool.RunnerStoreId, boot ?? spool.BootId, sequence),
        SampledAt = T0, PhysicalHostKey = "owned-host", BindingRevision = 1,
        SourceEpoch = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), MetricScope = "host",
        HostLogicalCores = 24, CpuPercent = 40, BusyCoreEquivalent = 9.6,
        Load1 = 24, Load5 = 12, Load15 = 6, LoadPerCore = 1,
    };
    internal static DateTimeOffset T0 => new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    internal static async Task EventuallyAsync(Func<bool> predicate)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(10, deadline.Token);
    }
}
