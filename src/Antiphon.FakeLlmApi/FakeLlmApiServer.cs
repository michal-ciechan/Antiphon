using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Antiphon.FakeLlmApi;

/// <summary>
/// In-process Kestrel recording stub on an ephemeral loopback port. Recording middleware runs
/// BEFORE routing so scripted errors and unmatched 404s still leave a complete record.
/// </summary>
public sealed class FakeLlmApiServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private FakeLlmApiServer(
        WebApplication app,
        RecordedRequestStore store,
        StubScript script,
        int listenPort,
        string baseUrl)
    {
        _app = app;
        Requests = store;
        Script = script;
        BaseUrl = baseUrl;
        ListenPort = listenPort;
    }

    public string BaseUrl { get; }
    public int ListenPort { get; }
    public RecordedRequestStore Requests { get; }
    public StubScript Script { get; }

    public static Task<FakeLlmApiServer> StartAsync(
        FakeLlmApiOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Claude && !options.Grok && !options.Codex)
            throw new ArgumentException("Enable at least one of Claude, Grok, or Codex.", nameof(options));

        var store = new RecordedRequestStore(options.JsonlPath);
        var script = new StubScript();
        return StartConfiguredAsync(options, store, script, cancellationToken);
    }

    private static async Task<FakeLlmApiServer> StartConfiguredAsync(
        FakeLlmApiOptions options,
        RecordedRequestStore store,
        StubScript script,
        CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            ApplicationName = typeof(FakeLlmApiServer).Assembly.FullName,
        });
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.ConfigureKestrel(k =>
        {
            // ListenLocalhost(0) refuses dynamic ports; bind 127.0.0.1:0 explicitly.
            k.Listen(IPAddress.Loopback, 0);
            k.Limits.MaxRequestBodySize = 32 * 1024 * 1024;
        });

        var app = builder.Build();

        // Closed over by middleware; filled after Start once Kestrel binds the ephemeral port.
        var portBox = new int[1];

        app.Use(async (ctx, next) =>
        {
            ctx.Request.EnableBuffering();
            string body;
            using (var reader = new StreamReader(
                       ctx.Request.Body,
                       Encoding.UTF8,
                       detectEncodingFromByteOrderMarks: false,
                       leaveOpen: true))
            {
                body = await reader.ReadToEndAsync(ctx.RequestAborted).ConfigureAwait(false);
                ctx.Request.Body.Position = 0;
            }

            var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in ctx.Request.Headers)
                headers[header.Key] = header.Value.Select(v => v ?? "").ToArray();

            var path = ctx.Request.Path.HasValue ? ctx.Request.Path.Value! : "/";
            var query = ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value! : "";
            var bodyBytes = Encoding.UTF8.GetByteCount(body);

            store.Record(new RecordedRequest(
                Seq: 0,
                UtcTimestamp: DateTimeOffset.UtcNow,
                Method: ctx.Request.Method,
                Path: path,
                QueryString: query,
                Headers: headers,
                Body: body,
                BodyByteLength: bodyBytes,
                ListenPort: portBox[0]));

            await next().ConfigureAwait(false);
        });

        if (options.Claude)
            ClaudeStubEndpoints.Map(app, script);
        if (options.Grok)
            GrokStubEndpoints.Map(app, script);
        if (options.Codex)
            CodexStubEndpoints.Map(app, script);

        app.MapFallback(async ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("""{"error":"stub unmatched path"}""", ctx.RequestAborted);
        });

        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        var server = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
        var addressFeature = server.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Kestrel did not expose server addresses.");
        var address = addressFeature.Addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("Kestrel bound no address.");
        var uri = new Uri(address);
        portBox[0] = uri.Port;
        var baseUrl = $"http://127.0.0.1:{uri.Port}";

        return new FakeLlmApiServer(app, store, script, uri.Port, baseUrl);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }
}
