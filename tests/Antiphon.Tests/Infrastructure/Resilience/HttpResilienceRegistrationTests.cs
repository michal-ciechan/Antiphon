using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Antiphon.Resilience;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.GitHub;
using Antiphon.Server.Infrastructure.IssueTrackers;
using Antiphon.Server.Infrastructure.Resilience;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure.Resilience;

public class HttpResilienceRegistrationTests
{
    [Test]
    public async Task Named_read_clients_recover_a_503_for_runner_github_issues_and_jira()
    {
        await Recover(ResilienceClientNames.RunnerRead, ResilienceOperations.RunnerGet, "sessions/1", RunnerJson());
        await Recover(ResilienceClientNames.GitHubRead, ResilienceOperations.GitHubUser, "user", """{"login":"octo"}""");
        await Recover(ResilienceClientNames.GitHubIssuesRead, ResilienceOperations.GitHubIssuesByIds, "repos/o/r/issues/1", "[]");
        await Recover(ResilienceClientNames.JiraRead, ResilienceOperations.JiraSearch, "rest/api/3/search", """{"issues":[]}""");
        await Recover(ResilienceClientNames.ProviderProbeRead, ResilienceOperations.ProviderProbeOpenAi, "v1/models", """{"data":[]}""");
    }

    [Test]
    public async Task Commands_stay_on_the_original_client_when_the_read_circuit_is_open()
    {
        var time = Clock();
        var reads = new ScriptHandler((_, _) => Task.FromResult(ResilienceTestHost.Status(HttpStatusCode.ServiceUnavailable)));
        var commands = new ScriptHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        await using var provider = ResilienceTestHost.Build(reads, ResilienceClientNames.RunnerRead, Tight(), time, new FixedResilienceJitter(1));
        var sut = Runner(provider, commands, time);
        await OpenReadCircuit(time, sut);
        var before = commands.Sends;

        await Should.ThrowAsync<Exception>(() => sut.StartAsync(Guid.NewGuid(), Spec(), CancellationToken.None));
        (commands.Sends - before).ShouldBe(1);
        before = commands.Sends;
        await Should.ThrowAsync<Exception>(() => sut.SendInputAsync(Guid.NewGuid(), "hi", CancellationToken.None));
        (commands.Sends - before).ShouldBe(1);
        before = commands.Sends;
        await Should.ThrowAsync<Exception>(() => sut.KillAsync(Guid.NewGuid(), CancellationToken.None));
        (commands.Sends - before).ShouldBe(1);
        before = commands.Sends;
        await Should.ThrowAsync<Exception>(() => sut.AttachHerdrAsync(Attach(), CancellationToken.None));
        (commands.Sends - before).ShouldBe(1);
        before = commands.Sends;
        // Disposal asks the read client for the capability first. An open read circuit is "no evidence",
        // so the command client must not be touched.
        await Should.ThrowAsync<Exception>(() => sut.DisposeHerdrPaneAsync(new HerdrPaneDisposalRequest(Guid.NewGuid(), Guid.NewGuid(), "because"), CancellationToken.None));
        commands.Sends.ShouldBe(before);
        await Should.ThrowAsync<Exception>(() => sut.ReadVerificationCustodyAsync(Binding(), seal: true, CancellationToken.None));
        (commands.Sends - before).ShouldBe(1);
    }

    [Test]
    public async Task Unnamed_and_event_clients_do_not_retry()
    {
        var handler = new ScriptHandler((_, _) => Task.FromResult(ResilienceTestHost.Status(HttpStatusCode.ServiceUnavailable)));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Clock());
        services.AddAntiphonResilience(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddHttpClient();
        services.AddHttpClient(SessionRunnerHttpClient.EventStreamClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();
        using var events = provider.GetRequiredService<IHttpClientFactory>().CreateClient(SessionRunnerHttpClient.EventStreamClientName);
        events.BaseAddress = new Uri("http://runner.test/");
        var response = await events.GetAsync("events");
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        handler.Sends.ShouldBe(1);
    }

    [Test]
    public async Task Read_requests_keep_the_primary_address_and_authorization()
    {
        var handler = new ScriptHandler((request, _) =>
        {
            request.Headers.Authorization.ShouldNotBeNull();
            request.Headers.Authorization!.Scheme.ShouldBe("Bearer");
            request.Headers.Authorization.Parameter.ShouldBe("token-marker");
            request.RequestUri!.Host.ShouldBe("api.github.test");
            return Task.FromResult(ResilienceTestHost.Status(HttpStatusCode.OK, """{"login":"octo"}"""));
        });
        await using var provider = ResilienceTestHost.Build(handler, ResilienceClientNames.GitHubRead);
        var primary = new HttpClient(new ScriptHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))))
        {
            BaseAddress = new Uri("https://api.github.test/"),
        };
        var github = new GitHubService(
            primary,
            Options.Create(new GithubSettings { BaseUrl = "https://api.github.test", PersonalAccessToken = "token-marker", Enabled = true }),
            NullLogger<GitHubService>.Instance,
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IOptionsMonitor<ResilienceSettings>>());
        var (ok, login, _) = await github.CheckConnectivityAsync(CancellationToken.None);
        ok.ShouldBeTrue();
        login.ShouldBe("octo");
        handler.Sends.ShouldBe(1);
    }

    [Test]
    public async Task Runner_list_and_git_connectivity_keep_their_short_deadlines()
    {
        var time = Clock();
        var reads = new ScriptHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return ResilienceTestHost.Status(HttpStatusCode.OK);
        });
        await using var provider = ResilienceTestHost.Build(reads, ResilienceClientNames.RunnerRead, time: time, jitter: new FixedResilienceJitter(1));
        var sut = Runner(provider, new ScriptHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))), time);
        var started = time.GetUtcNow();
        await Should.ThrowAsync<TaskCanceledException>(() =>
            ResilienceTestHost.Pump(time, sut.ListAsync(CancellationToken.None), TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(6)));
        var elapsed = time.GetUtcNow() - started;
        elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(3));
        elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(8));

        var gitHandler = new ScriptHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return ResilienceTestHost.Status(HttpStatusCode.OK);
        });
        await using var gitProvider = ResilienceTestHost.Build(gitHandler, ResilienceClientNames.GitConnectivityRead, time: time);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=u;Password=p").Options);
        var projects = new ProjectService(
            db,
            gitProvider.GetRequiredService<IHttpClientFactory>(),
            Options.Create(new GithubSettings()),
            NullLogger<ProjectService>.Instance,
            time: time,
            resilience: gitProvider.GetRequiredService<IOptionsMonitor<ResilienceSettings>>());
        var gitStarted = time.GetUtcNow();
        var result = await ResilienceTestHost.Pump(
            time,
            projects.TestGitConnectivityAsync("https://example.test/repo", CancellationToken.None),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(8));
        result.Success.ShouldBeFalse();
        (time.GetUtcNow() - gitStarted).ShouldBeLessThan(TimeSpan.FromSeconds(15));
    }

    [Test]
    public async Task Unknown_operations_and_dropped_commands_are_single_attempt()
    {
        var time = Clock();
        var reads = new ScriptHandler((_, _) => Task.FromResult(ResilienceTestHost.Status(HttpStatusCode.ServiceUnavailable)));
        await using var provider = ResilienceTestHost.Build(reads, ResilienceClientNames.GitHubRead, time: time);
        using var client = ResilienceTestHost.Client(provider, ResilienceClientNames.GitHubRead);
        using var post = new HttpRequestMessage(HttpMethod.Post, "repos/o/r/issues");
        ResilienceTestHost.Stamp(post, ResilienceOperations.GitHubUser);
        var response = await client.SendAsync(post);
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        reads.Sends.ShouldBe(1);

        var commands = new ScriptHandler((_, _) => throw new HttpRequestException("dropped"));
        var sut = Runner(provider, commands, time);
        var conditional = await sut.SendConditionalInputAsync(
            Guid.NewGuid(),
            new RunnerConditionalInputRequest(DateTime.UtcNow, 0, "body"),
            CancellationToken.None);
        conditional.Outcome.ShouldBe(ConditionalInputOutcomes.Unknown);
        commands.Sends.ShouldBe(1);
    }

    private static async Task Recover(string clientName, string operation, string path, string body)
    {
        var time = Clock();
        var step = 0;
        var handler = new ScriptHandler((_, _) =>
            Task.FromResult(ResilienceTestHost.Status(
                Interlocked.Increment(ref step) == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK,
                body)));
        await using var provider = ResilienceTestHost.Build(handler, clientName, time: time, jitter: new FixedResilienceJitter(1));
        using var client = ResilienceTestHost.Client(provider, clientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        ResilienceTestHost.Stamp(request, operation);
        var response = await ResilienceTestHost.Pump(time, client.SendAsync(request), TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(8));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        handler.Sends.ShouldBe(2);
    }

    private static async Task OpenReadCircuit(FakeTimeProvider time, SessionRunnerHttpClient sut)
    {
        for (var i = 0; i < 6; i++)
        {
            try
            {
                await ResilienceTestHost.Pump(time, sut.GetAsync(Guid.NewGuid(), CancellationToken.None), TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(3));
            }
            catch (HttpRequestException)
            {
                return;
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private static SessionRunnerHttpClient Runner(ServiceProvider provider, ScriptHandler commands, FakeTimeProvider time)
    {
        var primary = new HttpClient(commands) { BaseAddress = new Uri("http://runner.test/") };
        return new SessionRunnerHttpClient(
            primary,
            provider.GetRequiredService<IHttpClientFactory>(),
            Options.Create(new SessionRunnerSettings { BaseUrl = "http://runner.test", ListTimeoutSeconds = 3 }),
            time: time,
            resilience: provider.GetRequiredService<IOptionsMonitor<ResilienceSettings>>());
    }

    private static string RunnerJson() =>
        JsonSerializer.Serialize(
            new RunnerSessionDto(Guid.NewGuid(), 1, DateTime.UtcNow, "Running", null, "", 0),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static AgentLaunchSpec Spec() =>
        new("codex", Antiphon.Server.Domain.Enums.AgentKind.Codex, "codex", [], new Dictionary<string, string>(), Path.GetTempPath(), 80, 24);

    private static HerdrAttachRequest Attach() =>
        new(Guid.NewGuid(), "pane", "grok", "grok", 1, "none");

    private static VerificationExecutionBinding Binding() =>
        new(Guid.NewGuid(),
            new(Guid.NewGuid(), Guid.NewGuid(), new string('a', 40)),
            new(Guid.NewGuid(), DateTime.UtcNow),
            new(Path.GetTempPath(), Path.GetTempPath(), Path.GetTempPath(), Path.GetTempPath(), "feat/card", Guid.NewGuid()),
            VerificationCustodyBackends.LinuxCgroup,
            Guid.NewGuid());

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
            BreakDurationSeconds = 15,
        },
    };

    private static FakeTimeProvider Clock() => new(DateTimeOffset.Parse("2026-09-25T00:00:00Z"));
}
