using System.Net;
using System.Text;
using Antiphon.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure.Resilience;

[Category("Unit")]
[NotInParallel("antiphon-resilience-meter")]
public class HttpReadResilienceTests
{
    [Test]
    public async Task Service_unavailable_then_success_sends_twice_after_the_delay()
    {
        var time = Clock();
        var step = 0;
        var handler = new ScriptHandler((_, _) =>
            Task.FromResult(ResilienceTestHost.Status(
                Interlocked.Increment(ref step) == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK, "ok")))
        { Clock = () => time.GetUtcNow() };
        await using var provider = ResilienceTestHost.Build(handler, ResilienceClientNames.GitHubRead, time: time, jitter: new FixedResilienceJitter(1));
        using var client = ResilienceTestHost.Client(provider, ResilienceClientNames.GitHubRead);
        var response = ResilienceTestHost.Pump(time, Send(client, ResilienceOperations.GitHubUser), TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(8));
        var message = await response;
        message.StatusCode.ShouldBe(HttpStatusCode.OK);
        handler.Sends.ShouldBe(2);
        handler.ObservedAt.Count.ShouldBe(2);
        (handler.ObservedAt[1] - handler.ObservedAt[0]).ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Test]
    public async Task Internal_server_error_sends_once()
    {
        var handler = new ScriptHandler((_, _) => Task.FromResult(ResilienceTestHost.Status(HttpStatusCode.InternalServerError, "nope")));
        await using var provider = ResilienceTestHost.Build(handler, ResilienceClientNames.GitHubRead);
        using var client = ResilienceTestHost.Client(provider, ResilienceClientNames.GitHubRead);
        var response = await Send(client, ResilienceOperations.GitHubUser);
        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        handler.Sends.ShouldBe(1);
    }

    [Test]
    public async Task Retry_after_delta_is_the_wait()
    {
        var time = Clock();
        var step = 0;
        var handler = new ScriptHandler((_, _) =>
        {
            if (Interlocked.Increment(ref step) == 1)
            {
                var retry = ResilienceTestHost.Status(HttpStatusCode.TooManyRequests);
                retry.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
                return Task.FromResult(retry);
            }

            return Task.FromResult(ResilienceTestHost.Status(HttpStatusCode.OK));
        })
        { Clock = () => time.GetUtcNow() };
        await using var provider = ResilienceTestHost.Build(handler, ResilienceClientNames.GitHubRead, time: time, jitter: new FixedResilienceJitter(0));
        using var client = ResilienceTestHost.Client(provider, ResilienceClientNames.GitHubRead);
        var response = await ResilienceTestHost.Pump(
            time, Send(client, ResilienceOperations.GitHubUser), TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(8));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (handler.ObservedAt[1] - handler.ObservedAt[0]).ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Oversized_retry_after_returns_the_response_without_another_send()
    {
        var time = Clock();
        var handler = new ScriptHandler((_, _) =>
        {
            var retry = ResilienceTestHost.Status(HttpStatusCode.TooManyRequests, "later");
            retry.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(10));
            return Task.FromResult(retry);
        })
        { Clock = () => time.GetUtcNow() };
        await using var provider = ResilienceTestHost.Build(handler, ResilienceClientNames.GitHubRead, time: time);
        using var client = ResilienceTestHost.Client(provider, ResilienceClientNames.GitHubRead);
        var response = await Send(client, ResilienceOperations.GitHubUser);
        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        handler.Sends.ShouldBe(1);
        time.Advance(TimeSpan.FromSeconds(30));
        await Task.Delay(20);
        handler.Sends.ShouldBe(1);
    }

    [Test]
    public async Task Failed_response_is_disposed_before_the_next_attempt()
    {
        var time = Clock();
        WatchedResponse? failed = null;
        var step = 0;
        var handler = new ScriptHandler((_, _) =>
        {
            if (Interlocked.Increment(ref step) == 1)
            {
                failed = new WatchedResponse(HttpStatusCode.ServiceUnavailable);
                return Task.FromResult<HttpResponseMessage>(failed);
            }

            return Task.FromResult(ResilienceTestHost.Status(HttpStatusCode.OK));
        });
        await using var provider = ResilienceTestHost.Build(handler, ResilienceClientNames.GitHubRead, time: time, jitter: new FixedResilienceJitter(0));
        using var client = ResilienceTestHost.Client(provider, ResilienceClientNames.GitHubRead);
        var pumping = ResilienceTestHost.Pump(time, Send(client, ResilienceOperations.GitHubUser), TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(8));
        var done = await pumping;
        done.StatusCode.ShouldBe(HttpStatusCode.OK);
        failed.ShouldNotBeNull();
        failed.Disposed.ShouldBeTrue();
    }

    [Test]
    public async Task Admitted_read_body_is_replayed_unchanged()
    {
        var time = Clock();
        var body = Encoding.UTF8.GetBytes("same-body");
        var step = 0;
        var handler = new ScriptHandler((_, _) =>
            Task.FromResult(ResilienceTestHost.Status(
                Interlocked.Increment(ref step) == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)));
        await using var provider = ResilienceTestHost.Build(handler, ResilienceClientNames.GitHubRead, time: time, jitter: new FixedResilienceJitter(0));
        using var client = ResilienceTestHost.Client(provider, ResilienceClientNames.GitHubRead);
        using var request = new HttpRequestMessage(HttpMethod.Get, "user")
        {
            Content = new ByteArrayContent(body),
        };
        ResilienceTestHost.Stamp(request, ResilienceOperations.GitHubUser);
        var response = await ResilienceTestHost.Pump(time, client.SendAsync(request), TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(8));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        handler.Bodies.Count.ShouldBe(2);
        handler.Bodies[0].ShouldBe(body);
        handler.Bodies[1].ShouldBe(body);
    }

    [Test]
    public async Task Backoff_stays_inside_the_delay_cap()
    {
        var time = Clock();
        var settings = new ResilienceSettings
        {
            MaxRetryAttempts = 8,
            BaseDelayMilliseconds = 250,
            MaxDelayMilliseconds = 5_000,
            AttemptTimeoutSeconds = 10,
            TotalTimeoutSeconds = 120,
        };
        var step = 0;
        var handler = new ScriptHandler((_, _) =>
            Task.FromResult(ResilienceTestHost.Status(
                Interlocked.Increment(ref step) < 8 ? HttpStatusCode.BadGateway : HttpStatusCode.OK)))
        { Clock = () => time.GetUtcNow() };
        await using var provider = ResilienceTestHost.Build(
            handler, ResilienceClientNames.GitHubRead, settings, time, new FixedResilienceJitter(1));
        using var client = ResilienceTestHost.Client(provider, ResilienceClientNames.GitHubRead);
        var response = await ResilienceTestHost.Pump(
            time, Send(client, ResilienceOperations.GitHubUser), TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(15));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        handler.ObservedAt.Count.ShouldBeGreaterThan(3);
        for (var i = 1; i < handler.ObservedAt.Count; i++)
        {
            var waited = handler.ObservedAt[i] - handler.ObservedAt[i - 1];
            waited.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
            waited.ShouldBeLessThanOrEqualTo(TimeSpan.FromMilliseconds(settings.MaxDelayMilliseconds));
        }

        (handler.ObservedAt[^1] - handler.ObservedAt[^2]).ShouldBe(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Exhausted_retries_surface_the_last_response()
    {
        var time = Clock();
        var settings = new ResilienceSettings { MaxRetryAttempts = 2, BaseDelayMilliseconds = 250, MaxDelayMilliseconds = 500 };
        var handler = new ScriptHandler((_, _) => Task.FromResult(ResilienceTestHost.Status(HttpStatusCode.ServiceUnavailable, "still")));
        await using var provider = ResilienceTestHost.Build(handler, ResilienceClientNames.GitHubRead, settings, time, new FixedResilienceJitter(0));
        using var client = ResilienceTestHost.Client(provider, ResilienceClientNames.GitHubRead);
        var response = await ResilienceTestHost.Pump(
            time, Send(client, ResilienceOperations.GitHubUser), TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(8));
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).ShouldBe("still");
        handler.Sends.ShouldBe(3);
    }

    private static FakeTimeProvider Clock() => new(DateTimeOffset.Parse("2026-09-25T00:00:00Z"));

    private static Task<HttpResponseMessage> Send(HttpClient client, string operation)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "user");
        ResilienceTestHost.Stamp(request, operation);
        return client.SendAsync(request);
    }

    private sealed class WatchedResponse : HttpResponseMessage
    {
        public WatchedResponse(HttpStatusCode status) => StatusCode = status;

        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
