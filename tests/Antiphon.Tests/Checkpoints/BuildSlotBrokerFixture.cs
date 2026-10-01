using System.Net;
using System.Text;
using System.Text.Json;
using Antiphon.SessionRunner;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Antiphon.Tests.Checkpoints;

/// <summary>Real broker routes on an in-process HTTP server with a foreign PID namespace.</summary>
internal sealed class BuildSlotBrokerFixture : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpMessageHandler _handler;

    private BuildSlotBrokerFixture(WebApplication app)
    {
        _app = app;
        _handler = app.GetTestServer().CreateHandler();
        Http = new HttpClient(app.GetTestServer().CreateHandler())
            { BaseAddress = new Uri("http://slots.test/") };
    }

    public HttpClient Http { get; }
    public BuildSlotBroker Broker => _app.Services.GetRequiredService<BuildSlotBroker>();
    public HttpMessageHandler Handler => _handler;

    public static async Task<BuildSlotBrokerFixture> StartAsync(int budget = 2, int cpu = 6, int renewEvery = 1)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Test" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SessionRunner:BuildSlots:Enabled"] = "true",
            ["SessionRunner:BuildSlots:MaxConcurrent"] = budget.ToString(),
            ["SessionRunner:BuildSlots:MaxCpuCount"] = cpu.ToString(),
            ["SessionRunner:BuildSlots:MinAvailableMemoryMb"] = "0",
            ["SessionRunner:BuildSlots:HolderLiveness"] = "renew",
            ["SessionRunner:BuildSlots:RenewEverySeconds"] = renewEvery.ToString(),
            ["SessionRunner:BuildSlots:RenewGraceSeconds"] = "90",
            ["SessionRunner:BuildSlots:RetryAfterMs"] = "5",
        });
        builder.Services.AddSingleton<IProcessLivenessProbe>(new ForeignLiveness());
        builder.Services.AddSingleton<IHostMemoryProbe>(new Memory());
        builder.Services.AddBuildSlotBroker(builder.Configuration);
        var app = builder.Build();
        app.MapBuildSlotRoutes();
        await app.StartAsync();
        return new BuildSlotBrokerFixture(app);
    }

    public HttpMessageHandler Recording(bool omitStart = false, int omitStartAfter = 0,
        int invalidateGrantAfter = 0) =>
        new Recorder(_app.GetTestServer().CreateHandler(), omitStart, omitStartAfter, invalidateGrantAfter);

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        _handler.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    public sealed class Recorder(HttpMessageHandler inner, bool omitStart, int omitStartAfter,
        int invalidateGrantAfter) : DelegatingHandler(inner)
    {
        public List<(string Method, string Path, string Body)> Calls { get; } = [];
        private int _acquisitions;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (Calls) Calls.Add((request.Method.Method, request.RequestUri!.AbsolutePath, body));
            var acquire = request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/build-slots";
            var index = acquire ? Interlocked.Increment(ref _acquisitions) : 0;
            if (acquire && (omitStart || omitStartAfter > 0 && index > omitStartAfter))
            {
                using var doc = JsonDocument.Parse(body);
                var data = doc.RootElement.EnumerateObject()
                    .Where(item => item.Name != "processStartUtc")
                    .ToDictionary(item => item.Name, item => item.Value.Clone());
                request.Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");
            }
            var response = await base.SendAsync(request, cancellationToken);
            if (acquire && invalidateGrantAfter > 0 && index > invalidateGrantAfter
                && response.StatusCode == HttpStatusCode.OK)
            {
                var original = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(original);
                var data = doc.RootElement.EnumerateObject()
                    .ToDictionary(item => item.Name, item => item.Value.Clone());
                data["maxCpuCount"] = JsonSerializer.SerializeToElement(0);
                response.Content.Dispose();
                response.Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");
            }
            return response;
        }
    }

    private sealed class ForeignLiveness : IProcessLivenessProbe
    {
        public bool IsAlive(int pid, DateTime startedAt) => false;
        public string? TryGetProcessName(int pid) => null;
        public DateTime? TryGetStartTimeUtc(int pid) => null;
    }

    private sealed class Memory : IHostMemoryProbe { public long? AvailableBytes => 64L * 1024 * 1024 * 1024; }
}
