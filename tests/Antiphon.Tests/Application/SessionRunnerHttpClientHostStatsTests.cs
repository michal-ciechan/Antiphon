using System.Net;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class SessionRunnerHttpClientHostStatsTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Count);
            return send(request, ct);
        }
    }

    private sealed class Factory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Host stats must use the typed client.");
    }

    private static SessionRunnerHttpClient Client(Handler handler, TimeProvider? time = null) =>
        new(new HttpClient(handler), new Factory(),
            Options.Create(new SessionRunnerSettings { BaseUrl = "http://runner.test" }),
            time: time, hostStats: Options.Create(new HostStatsSettings { RequestTimeoutMs = 3000 }));

    [Test]
    public async Task Host_stats_read_is_one_attempt()
    {
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        await Should.ThrowAsync<HttpRequestException>(() => Client(handler).GetHostStatsAsync(CancellationToken.None));
        handler.Count.ShouldBe(1);
    }

    [Test]
    public async Task Not_found_maps_to_unsupported()
    {
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        await Should.ThrowAsync<HostStatsUnsupportedException>(() => Client(handler).GetHostStatsAsync(CancellationToken.None));
        handler.Count.ShouldBe(1);
    }

    [Test]
    public async Task Hung_runner_is_cancelled_at_the_request_timeout()
    {
        var time = new FakeTimeProvider();
        var handler = new Handler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var pending = Client(handler, time).GetHostStatsAsync(CancellationToken.None);
        while (handler.Count == 0)
            await Task.Yield();
        time.Advance(TimeSpan.FromMilliseconds(3000));
        await Should.ThrowAsync<TimeoutException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10)));
        handler.Count.ShouldBe(1);
    }
}
