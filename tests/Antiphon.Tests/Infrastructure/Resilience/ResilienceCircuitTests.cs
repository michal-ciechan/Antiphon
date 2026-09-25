using System.Net;
using Antiphon.Resilience;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Resilience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure.Resilience;

[Category("Unit")]
[NotInParallel("antiphon-resilience-meter")]
public class ResilienceCircuitTests
{
    [Test]
    public async Task Qualifying_failures_open_the_circuit_and_the_next_call_sends_nothing()
    {
        var (provider, handler, time) = OpenableHost();
        await using var _ = provider;
        using var client = ResilienceTestHost.Client(provider, ResilienceClientNames.GitHubRead);
        await PumpUntilOpen(time, client, handler);
        var after = handler.Sends;
        await Should.ThrowAsync<HttpRequestException>(() => Send(client));
        handler.Sends.ShouldBe(after);
    }

    [Test]
    public async Task Half_open_probe_is_bounded_and_success_closes_the_circuit()
    {
        var (provider, handler, time) = OpenableHost();
        await using var _ = provider;
        using var client = ResilienceTestHost.Client(provider, ResilienceClientNames.GitHubRead);
        await PumpUntilOpen(time, client, handler);
        time.Advance(TimeSpan.FromSeconds(1));
        handler.Next = () => ResilienceTestHost.Status(HttpStatusCode.OK, "back");
        var recovered = await Send(client);
        recovered.StatusCode.ShouldBe(HttpStatusCode.OK);
        var again = await Send(client);
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Another_dependency_stays_callable_while_the_first_circuit_is_open()
    {
        var settings = Tight();
        var time = Clock();
        var github = new ScriptHandler((_, _) => Task.FromResult(ResilienceTestHost.Status(HttpStatusCode.ServiceUnavailable)));
        var jira = new ScriptHandler((_, _) => Task.FromResult(ResilienceTestHost.Status(HttpStatusCode.OK, "jira")));
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton<IResilienceJitter>(new FixedResilienceJitter(0));
        services.AddLogging();
        services.AddAntiphonResilience(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.PostConfigure<ResilienceSettings>(target =>
        {
            target.Enabled = settings.Enabled;
            target.TotalTimeoutSeconds = settings.TotalTimeoutSeconds;
            target.AttemptTimeoutSeconds = settings.AttemptTimeoutSeconds;
            target.MaxRetryAttempts = settings.MaxRetryAttempts;
            target.BaseDelayMilliseconds = settings.BaseDelayMilliseconds;
            target.MaxDelayMilliseconds = settings.MaxDelayMilliseconds;
            target.CircuitBreaker = settings.CircuitBreaker;
            target.Http = settings.Http;
            target.Database = settings.Database;
        });
        services.AddHttpClient(ResilienceClientNames.GitHubRead).ConfigurePrimaryHttpMessageHandler(() => github);
        services.AddHttpClient(ResilienceClientNames.JiraRead).ConfigurePrimaryHttpMessageHandler(() => jira);
        await using var provider = services.BuildServiceProvider();
        using var gitClient = ResilienceTestHost.Client(provider, ResilienceClientNames.GitHubRead);
        using var jiraClient = ResilienceTestHost.Client(provider, ResilienceClientNames.JiraRead);
        await PumpUntilOpen(time, gitClient, github);
        var request = new HttpRequestMessage(HttpMethod.Get, "rest/api/3/search");
        ResilienceTestHost.Stamp(request, ResilienceOperations.JiraSearch);
        var response = await jiraClient.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        jira.Sends.ShouldBe(1);
    }

    [Test]
    public async Task Evicted_authority_starts_a_fresh_circuit()
    {
        var settings = Tight();
        settings.Http.MaxAuthorityPipelines = 2;
        settings.CircuitBreaker.BreakDurationSeconds = 120;
        var time = Clock();
        var handler = new ScriptHandler((_, _) => Task.FromResult(ResilienceTestHost.Status(HttpStatusCode.ServiceUnavailable)));
        await using var provider = ResilienceTestHost.Build(
            handler, ResilienceClientNames.JiraRead, settings, time, new FixedResilienceJitter(0));
        using var client = ResilienceTestHost.Client(provider, ResilienceClientNames.JiraRead);
        await OpenAuthority(time, client, handler, "https://a.test/a");
        var blocked = handler.Sends;
        var blockedCall = await Should.ThrowAsync<HttpRequestException>(() => SendTo(client, "https://a.test/a"));
        blockedCall.Message.ShouldContain("circuit is open");
        handler.Sends.ShouldBe(blocked);

        handler.Next = () => ResilienceTestHost.Status(HttpStatusCode.OK, "other");
        (await SendTo(client, "https://b.test/b")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendTo(client, "https://c.test/c")).StatusCode.ShouldBe(HttpStatusCode.OK);
        provider.GetRequiredService<ResiliencePipelineCache>()
            .ActiveHttpPipelines(ResilienceDependencies.Jira).ShouldBe(2);

        handler.Next = () => ResilienceTestHost.Status(HttpStatusCode.OK, "fresh");
        var fresh = await SendTo(client, "https://a.test/a");
        fresh.StatusCode.ShouldBe(HttpStatusCode.OK);
        handler.Sends.ShouldBe(blocked + 3);
        handler.Paths[^1].ShouldBe("/a");
    }

    [Test]
    public async Task Non_transient_responses_do_not_open_the_circuit()
    {
        var (provider, handler, _) = OpenableHost();
        await using var _ = provider;
        handler.Next = () => ResilienceTestHost.Status(HttpStatusCode.InternalServerError);
        using var client = ResilienceTestHost.Client(provider, ResilienceClientNames.GitHubRead);
        for (var i = 0; i < 6; i++)
        {
            var response = await Send(client);
            response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        }

        handler.Sends.ShouldBe(6);
    }

    [Test]
    public async Task Zero_queue_limiter_rejects_without_retry_and_database_scope_is_disposed_before_delay()
    {
        var settings = Tight();
        settings.Http.MaxConcurrentAttempts = 1;
        var time = Clock();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new ScriptHandler(async (_, ct) =>
        {
            await release.Task.WaitAsync(ct);
            return ResilienceTestHost.Status(HttpStatusCode.OK);
        });
        await using var provider = ResilienceTestHost.Build(handler, ResilienceClientNames.GitHubRead, settings, time);
        using var client = ResilienceTestHost.Client(provider, ResilienceClientNames.GitHubRead);
        var first = Send(client);
        await Task.Delay(50);
        handler.Sends.ShouldBe(1);
        await Should.ThrowAsync<HttpRequestException>(() => Send(client));
        handler.Sends.ShouldBe(1);
        release.TrySetResult();
        (await first).StatusCode.ShouldBe(HttpStatusCode.OK);

        var dbTime = Clock();
        var observed = new List<bool>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(dbTime);
        services.AddSingleton<IResilienceJitter>(new FixedResilienceJitter(1));
        services.AddAntiphonResilience(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.PostConfigure<ResilienceSettings>(target =>
        {
            target.BaseDelayMilliseconds = 1_000;
            target.MaxDelayMilliseconds = 1_000;
            target.MaxRetryAttempts = 2;
            target.AttemptTimeoutSeconds = 5;
            target.TotalTimeoutSeconds = 30;
            target.CircuitBreaker.SamplingDurationSeconds = 10;
        });
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql("Host=127.0.0.1;Port=1;Database=resilience_unit;Username=u;Password=p"));
        services.AddSingleton(new DatabaseResilienceName("resilience_unit"));
        services.AddSingleton<IDatabaseReadFault>(new OneShot(new PostgresException("down", "ERROR", "ERROR", "08006")));
        services.AddSingleton<IDatabaseReadObserver>(new RecordingObserver(observed));
        services.AddSingleton(sp => new DatabaseResilienceExecutor(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<DatabaseAttemptExecutor>(),
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<ResilienceSettings>>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<DatabaseResilienceName>(),
            sp.GetRequiredService<IDatabaseReadFault>(),
            sp.GetRequiredService<IDatabaseReadObserver>()));
        await using var dbProvider = services.BuildServiceProvider();
        var executor = dbProvider.GetRequiredService<DatabaseResilienceExecutor>();
        var read = executor.ExecuteReadAsync(
            ResilienceOperations.LlmProvidersList,
            (_, _) => Task.FromResult(new List<int>()),
            CancellationToken.None);
        await Task.Delay(50);
        observed.Count.ShouldBe(1);
        observed[0].ShouldBeTrue();
        dbTime.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(50);
        observed.Count.ShouldBe(2);
        (await read).ShouldBeEmpty();
    }

    private static async Task PumpUntilOpen(FakeTimeProvider time, HttpClient client, ScriptHandler handler)
    {
        for (var i = 0; i < 8; i++)
        {
            try
            {
                await ResilienceTestHost.Pump(time, Send(client), TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(3));
            }
            catch (HttpRequestException)
            {
                return;
            }
        }

        throw new InvalidOperationException($"Circuit stayed closed after {handler.Sends} sends.");
    }

    private static (ServiceProvider Provider, ScriptHandler Handler, FakeTimeProvider Time) OpenableHost()
    {
        var time = Clock();
        var handler = new ScriptHandler((_, _) => Task.FromResult(ResilienceTestHost.Status(HttpStatusCode.OK)));
        handler.Next = () => ResilienceTestHost.Status(HttpStatusCode.ServiceUnavailable);
        var provider = ResilienceTestHost.Build(handler, ResilienceClientNames.GitHubRead, Tight(), time, new FixedResilienceJitter(0));
        return (provider, handler, time);
    }

    private static ResilienceSettings Tight() => new()
    {
        TotalTimeoutSeconds = 30,
        AttemptTimeoutSeconds = 2,
        MaxRetryAttempts = 1,
        BaseDelayMilliseconds = 10,
        MaxDelayMilliseconds = 10,
        CircuitBreaker = new ResilienceCircuitBreakerSettings
        {
            FailureRatio = 0.5,
            MinimumThroughput = 2,
            SamplingDurationSeconds = 10,
            BreakDurationSeconds = 1,
        },
    };

    private static FakeTimeProvider Clock() => new(DateTimeOffset.Parse("2026-09-25T00:00:00Z"));

    private static async Task OpenAuthority(
        FakeTimeProvider time, HttpClient client, ScriptHandler handler, string uri)
    {
        for (var i = 0; i < 8; i++)
        {
            var before = handler.Sends;
            try
            {
                using var response = await ResilienceTestHost.Pump(
                    time, SendTo(client, uri), TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(3));
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("circuit is open", StringComparison.Ordinal))
            {
                handler.Sends.ShouldBe(before);
                return;
            }
        }

        throw new InvalidOperationException($"Circuit stayed closed after {handler.Sends} sends.");
    }

    private static Task<HttpResponseMessage> Send(HttpClient client)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "user");
        ResilienceTestHost.Stamp(request, ResilienceOperations.GitHubUser);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SendTo(HttpClient client, string uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        ResilienceTestHost.Stamp(request, ResilienceOperations.JiraSearch);
        return client.SendAsync(request);
    }

    private sealed class OneShot(Exception exception) : IDatabaseReadFault
    {
        private int _remaining = 1;

        public Exception? Consume() => Interlocked.Decrement(ref _remaining) >= 0 ? exception : null;
    }

    private sealed class RecordingObserver(List<bool> disposed) : IDatabaseReadObserver
    {
        public void OnAttemptEnded(AppDbContext context)
        {
            var thrown = false;
            try
            {
                _ = context.LlmProviders.Local.Count;
            }
            catch (ObjectDisposedException)
            {
                thrown = true;
            }

            disposed.Add(thrown);
        }
    }
}
