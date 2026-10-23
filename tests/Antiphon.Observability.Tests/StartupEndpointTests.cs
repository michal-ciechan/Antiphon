using System.Net;
using System.Net.Http.Json;
using System.Diagnostics;
using Antiphon.Observability;
using Antiphon.Server.Api.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Observability.Tests;

[Category("Integration")]
public sealed class StartupEndpointTests
{
    [Test]
    public async Task V5_Diagnostics_returns_the_live_instance_without_payloads(CancellationToken cancellationToken)
    {
        var log = new CaptureLogger();
        using var timing = new LiveTiming(log);
        using (var phase = timing.BeginStartup(StartupOperation.Seed)) phase.Complete();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(timing);
        await using var app = builder.Build();
        app.MapVersionEndpoints();
        await app.StartAsync(cancellationToken);
        using var http = app.GetTestClient();
        using var response = await http.GetAsync("/api/diagnostics/startup", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<StartupSnapshot>(cancellationToken))!;
        body.RunId.ShouldBe(timing.ReadStartup().RunId);
        body.Phases.Single().Operation.ShouldBe("seed");
        body.Phases.Single().Outcome.ShouldBe("completed");
        await app.StopAsync(cancellationToken);
    }

    [Test]
    public async Task V6_Http_span_omits_uri_headers_body_and_exception_text(CancellationToken cancellationToken)
    {
        var log = new CaptureLogger();
        using var timing = new LiveTiming(log);
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == LiveTiming.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => { lock (stopped) stopped.Add(a); }
        };
        ActivitySource.AddActivityListener(listener);
        using var handler = new LiveTimingHttpHandler(timing) { InnerHandler = new RejectHandler() };
        using var http = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://secret-canary.invalid/private?token=secret-canary");
        request.Headers.Add("Authorization", "Bearer secret-canary");
        request.Content = new StringContent("secret-canary");
        await Should.ThrowAsync<IOException>(() => http.SendAsync(request, cancellationToken));
        Activity span;
        lock (stopped) span = stopped.Single(a => a.GetTagItem("run.id")?.ToString() == timing.ReadStartup().RunId);
        span.OperationName.ShouldBe("http");
        span.Status.ShouldBe(ActivityStatusCode.Error);
        string.Join(" ", span.TagObjects.Select(x => $"{x.Key}={x.Value}")).ShouldNotContain("secret-canary");
        log.Rows.ShouldBeEmpty();
    }

    private sealed class RejectHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new IOException("secret-canary"));
    }
}
