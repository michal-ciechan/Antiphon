using System.Net;
using Antiphon.Resilience;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure.Resilience;

[Category("Unit")]
[NotInParallel("antiphon-resilience-meter")]
public class ResilienceBudgetTests
{
    [Test]
    public async Task Persistent_503_stops_at_the_configured_total_boundary()
    {
        var time = Clock();
        var settings = new ResilienceSettings
        {
            MaxRetryAttempts = 100,
            BaseDelayMilliseconds = 250,
            MaxDelayMilliseconds = 5_000,
            CircuitBreaker = new ResilienceCircuitBreakerSettings { MinimumThroughput = 1000, SamplingDurationSeconds = 30 },
        };
        var handler = new ScriptHandler((_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(ResilienceTestHost.Status(HttpStatusCode.ServiceUnavailable));
        })
        { Clock = () => time.GetUtcNow() };
        await using var provider = ResilienceTestHost.Build(
            handler, ResilienceClientNames.RunnerRead, settings, time, new FixedResilienceJitter(0));
        using var client = ResilienceTestHost.Client(provider, ResilienceClientNames.RunnerRead);
        var started = time.GetUtcNow();
        var work = Send(client, ResilienceOperations.RunnerGet);
        // Stop one second before the total budget so observing cancellation cannot advance the clock.
        var realLimit = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (time.GetUtcNow() - started < TimeSpan.FromSeconds(119) && !work.IsCompleted)
        {
            if (DateTime.UtcNow >= realLimit)
                throw new TimeoutException("fake clock did not reach 119s before the real-time limit");
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(1);
        }

        work.IsCompleted.ShouldBeFalse("the read must still be running one second before the total budget");
        (time.GetUtcNow() - started).ShouldBe(TimeSpan.FromSeconds(119));
        time.Advance(TimeSpan.FromSeconds(1));
        await Should.ThrowAsync<TaskCanceledException>(() => work.WaitAsync(TimeSpan.FromSeconds(10)));
        var elapsed = time.GetUtcNow() - started;
        elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(120));
        elapsed.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(121));
        handler.Sends.ShouldBeGreaterThan(1);
        handler.ObservedAt[^1].ShouldBeLessThanOrEqualTo(started + TimeSpan.FromSeconds(120));
    }

    [Test]
    public async Task Slow_first_attempt_consumes_the_same_budget()
    {
        var time = Clock();
        var settings = new ResilienceSettings
        {
            TotalTimeoutSeconds = 30,
            AttemptTimeoutSeconds = 10,
            MaxRetryAttempts = 20,
            CircuitBreaker = new ResilienceCircuitBreakerSettings { SamplingDurationSeconds = 20, MinimumThroughput = 10 },
        };
        settings.CircuitBreaker.MinimumThroughput = 1000;
        var step = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new ScriptHandler(async (_, ct) =>
        {
            if (Interlocked.Increment(ref step) == 1)
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }

            return ResilienceTestHost.Status(HttpStatusCode.ServiceUnavailable);
        });
        await using var provider = ResilienceTestHost.Build(handler, ResilienceClientNames.RunnerRead, settings, time, new FixedResilienceJitter(1));
        using var client = ResilienceTestHost.Client(provider, ResilienceClientNames.RunnerRead);
        var budget = ResilienceBudget.Start(time, settings, profile: null);
        var started = time.GetUtcNow();
        using var first = new HttpRequestMessage(HttpMethod.Get, "sessions/1");
        ResilienceTestHost.Stamp(first, ResilienceOperations.RunnerGet, budget);
        var firstSend = client.SendAsync(first);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Should.ThrowAsync<TaskCanceledException>(() =>
            ResilienceTestHost.Pump(time, firstSend, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(8)));
        var afterAttempt = time.GetUtcNow() - started;
        afterAttempt.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(10));
        afterAttempt.ShouldBeLessThan(TimeSpan.FromSeconds(12));
        handler.Sends.ShouldBe(1);
        using var second = new HttpRequestMessage(HttpMethod.Get, "sessions/1");
        ResilienceTestHost.Stamp(second, ResilienceOperations.RunnerGet, budget);
        var secondSend = client.SendAsync(second);
        var sawSecond = DateTime.UtcNow.AddSeconds(5);
        while (handler.Sends < 2 && DateTime.UtcNow < sawSecond)
            await Task.Delay(10);
        handler.Sends.ShouldBeGreaterThan(1);
        await Should.ThrowAsync<TaskCanceledException>(() =>
            ResilienceTestHost.Pump(time, secondSend, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(12)));
        var elapsed = time.GetUtcNow() - started;
        elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(30));
        elapsed.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(32));
    }

    [Test]
    public async Task Body_read_is_inside_the_attempt_timeout()
    {
        var time = Clock();
        var settings = Fast(attemptSeconds: 2, totalSeconds: 10);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new ScriptHandler((_, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new BlockingContent(release.Task, ct),
            };
            return Task.FromResult(response);
        });
        await using var provider = ResilienceTestHost.Build(handler, ResilienceClientNames.GitHubRead, settings, time);
        using var client = ResilienceTestHost.Client(provider, ResilienceClientNames.GitHubRead);
        var work = Send(client, ResilienceOperations.GitHubUser);
        await Should.ThrowAsync<TaskCanceledException>(() =>
            ResilienceTestHost.Pump(time, work, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(8)));
        handler.Sends.ShouldBe(1);
        release.TrySetResult();
    }

    [Test]
    public async Task Caller_cancellation_during_delay_sends_no_further_request()
    {
        var time = Clock();
        var handler = new ScriptHandler((_, _) => Task.FromResult(ResilienceTestHost.Status(HttpStatusCode.ServiceUnavailable)));
        await using var provider = ResilienceTestHost.Build(
            handler, ResilienceClientNames.GitHubRead, time: time, jitter: new FixedResilienceJitter(1));
        using var client = ResilienceTestHost.Client(provider, ResilienceClientNames.GitHubRead);
        using var cts = new CancellationTokenSource();
        var request = new HttpRequestMessage(HttpMethod.Get, "user");
        ResilienceTestHost.Stamp(request, ResilienceOperations.GitHubUser);
        var work = client.SendAsync(request, cts.Token);
        await Task.Delay(30);
        handler.Sends.ShouldBe(1);
        cts.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => work);
        time.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(30);
        handler.Sends.ShouldBe(1);
    }

    [Test]
    public async Task Narrower_parent_deadlines_win()
    {
        foreach (var seconds in new[] { 3, 5, 10 })
        {
            var time = Clock();
            var settings = new ResilienceSettings
            {
                TotalTimeoutSeconds = 120,
                AttemptTimeoutSeconds = 1,
                MaxRetryAttempts = 50,
                CircuitBreaker = new ResilienceCircuitBreakerSettings { SamplingDurationSeconds = 30 },
            };
            var handler = new ScriptHandler((_, _) => Task.FromResult(ResilienceTestHost.Status(HttpStatusCode.ServiceUnavailable)))
            { Clock = () => time.GetUtcNow() };
            await using var provider = ResilienceTestHost.Build(handler, ResilienceClientNames.RunnerRead, settings, time, new FixedResilienceJitter(0));
            using var client = ResilienceTestHost.Client(provider, ResilienceClientNames.RunnerRead);
            var budget = ResilienceBudget.Start(time, settings, profile: null, TimeSpan.FromSeconds(seconds));
            var request = new HttpRequestMessage(HttpMethod.Get, "sessions");
            ResilienceTestHost.Stamp(request, ResilienceOperations.RunnerList, budget);
            var started = time.GetUtcNow();
            await Should.ThrowAsync<TaskCanceledException>(() =>
                ResilienceTestHost.Pump(time, client.SendAsync(request), TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(8)));
            var elapsed = time.GetUtcNow() - started;
            elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(seconds));
            elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(seconds + 2));
        }
    }

    [Test]
    public async Task Nested_pages_share_one_deadline()
    {
        var time = Clock();
        var settings = new ResilienceSettings
        {
            TotalTimeoutSeconds = 20,
            AttemptTimeoutSeconds = 19,
            MaxRetryAttempts = 1,
            CircuitBreaker = new ResilienceCircuitBreakerSettings { MinimumThroughput = 1000, SamplingDurationSeconds = 40 },
        };
        var step = 0;
        var handler = new ScriptHandler(async (_, ct) =>
        {
            if (Interlocked.Increment(ref step) == 2)
                await Task.Delay(Timeout.Infinite, ct);
            return ResilienceTestHost.Status(HttpStatusCode.OK);
        });
        await using var provider = ResilienceTestHost.Build(handler, ResilienceClientNames.GitHubRead, settings, time);
        using var client = ResilienceTestHost.Client(provider, ResilienceClientNames.GitHubRead);
        var budget = ResilienceBudget.Start(time, settings, profile: null);
        using var first = new HttpRequestMessage(HttpMethod.Get, "user/repos?page=1");
        ResilienceTestHost.Stamp(first, ResilienceOperations.GitHubRepositories, budget);
        (await client.SendAsync(first)).StatusCode.ShouldBe(HttpStatusCode.OK);
        time.Advance(TimeSpan.FromSeconds(15));
        using var second = new HttpRequestMessage(HttpMethod.Get, "user/repos?page=2");
        ResilienceTestHost.Stamp(second, ResilienceOperations.GitHubRepositories, budget);
        await Should.ThrowAsync<TaskCanceledException>(() =>
            ResilienceTestHost.Pump(time, client.SendAsync(second), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8)));
        var elapsed = time.GetUtcNow() - DateTimeOffset.Parse("2026-09-25T00:00:00Z");
        elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(20));
        elapsed.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(24));
        handler.Sends.ShouldBe(2);
    }

    [Test]
    public async Task Hanging_attempt_honors_the_attempt_timeout_without_overlapping_work()
    {
        var time = Clock();
        var settings = Fast(attemptSeconds: 2, totalSeconds: 30);
        var inFlight = 0;
        var maxInFlight = 0;
        var handler = new ScriptHandler(async (_, ct) =>
        {
            var now = Interlocked.Increment(ref inFlight);
            maxInFlight = Math.Max(maxInFlight, now);
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
                return ResilienceTestHost.Status(HttpStatusCode.OK);
            }
            finally
            {
                Interlocked.Decrement(ref inFlight);
            }
        });
        await using var provider = ResilienceTestHost.Build(handler, ResilienceClientNames.GitHubRead, settings, time);
        using var client = ResilienceTestHost.Client(provider, ResilienceClientNames.GitHubRead);
        await Should.ThrowAsync<TaskCanceledException>(() =>
            ResilienceTestHost.Pump(time, Send(client, ResilienceOperations.GitHubUser), TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(8)));
        handler.Sends.ShouldBe(1);
        maxInFlight.ShouldBe(1);
        inFlight.ShouldBe(0);
    }

    private static FakeTimeProvider Clock() => new(DateTimeOffset.Parse("2026-09-25T00:00:00Z"));

    private static ResilienceSettings Fast(int attemptSeconds, int totalSeconds) => new()
    {
        TotalTimeoutSeconds = totalSeconds,
        AttemptTimeoutSeconds = attemptSeconds,
        MaxRetryAttempts = 6,
        CircuitBreaker = new ResilienceCircuitBreakerSettings
        {
            SamplingDurationSeconds = Math.Max(attemptSeconds * 2, 2),
            MinimumThroughput = 10,
        },
    };

    private static Task<HttpResponseMessage> Send(HttpClient client, string operation)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "sessions/1");
        ResilienceTestHost.Stamp(request, operation);
        return client.SendAsync(request);
    }

    private sealed class BlockingContent : HttpContent
    {
        private readonly Task _gate;
        private readonly CancellationToken _cancellation;

        public BlockingContent(Task gate, CancellationToken cancellation)
        {
            _gate = gate;
            _cancellation = cancellation;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Wait(_cancellation);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            var linked = CancellationTokenSource.CreateLinkedTokenSource(_cancellation, cancellationToken);
            return Wait(linked.Token);
        }

        private async Task Wait(CancellationToken cancellationToken) =>
            await _gate.WaitAsync(cancellationToken);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
