using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Api.Middleware;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Security;
using Antiphon.Tests.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Api;

/// <summary>
/// CARD-0650 S4 repair 4 (review 8adb4cd6). <c>POST /api/sessions/{id}/expectation-hold/release</c>
/// is an operator surface: it needs the CARD-0658 operator token, whatever the request looks like,
/// and records who released the hold. The route runs over HTTP against the real queue service and
/// the fake terminal of <see cref="ExpectationDeliveryFixture"/>.
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public sealed class ExpectationHoldReleaseEndpointTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task C650_Release_without_the_operator_token_is_refused_and_releases_nothing()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        await using var host = await ReleaseHost.StartAsync(f);
        var task = await f.SubjectTaskAsync();
        var stranded = await StrandAsync(f, task);

        foreach (var (name, token) in new[] { ("no token", (string?)null), ("wrong token", "0123456789abcdef"), ("empty token", "") })
        {
            using var refused = await host.PostAsync(f.SessionId, "looked at the pane", token, proxied: true);
            ((int)refused.StatusCode).ShouldBeOneOf([401, 403], name);
            (await CodeAsync(refused)).ShouldBe("operator_token_required", name);
        }

        (await f.ReloadAsync(stranded.Id)).AttemptState.ShouldBe(ExpectationAttemptState.Unconfirmed, "a refused release releases nothing");
        (await ReleasedCommentsAsync(f)).ShouldBeEmpty("a refused release audits nothing");
        await Should.ThrowAsync<ConflictException>(() => f.Harness.Queue.EnqueueAsync(
            f.SessionId, "operator send now", MessageSendMode.Now, CancellationToken.None));
        f.Harness.Adapter.SubmittedBodies.ShouldBeEmpty("the hold still stands");

        // The operator token releases it, and the audit names the authenticated operator.
        using var granted = await host.PostAsync(f.SessionId, "looked at the pane", host.Token, proxied: true);
        granted.StatusCode.ShouldBe(HttpStatusCode.OK, await granted.Content.ReadAsStringAsync());
        (await ResultAsync(granted)).ReleasedNudgeIds.ShouldBe([stranded.Id]);
        (await f.ReloadAsync(stranded.Id)).AttemptState.ShouldBe(ExpectationAttemptState.Released);
        var comment = (await ReleasedCommentsAsync(f)).ShouldHaveSingleItem();
        comment.Body.ShouldContain("Released by admin (operator token)", Case.Sensitive);
        comment.Body.ShouldNotContain(host.Token, Case.Sensitive, "the credential is never recorded");
        comment.Author.ShouldBe("operator:admin (operator token)");
        await using (var db = f.Db())
        {
            (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == task && e.Type == AgentTaskEventType.Check
                && e.Detail!.Contains("Released by admin (operator token)"))).ShouldBe(1);
        }
    }

    [Test]
    public async Task C650_Release_of_another_session_releases_nothing()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        await using var host = await ReleaseHost.StartAsync(f);
        var stranded = await StrandAsync(f, await f.SubjectTaskAsync());
        var other = await f.AddSessionAsync(ownedByStandingAgent: true, makeCurrent: false);

        using var wrong = await host.PostAsync(other, "wrong pane", host.Token);
        wrong.StatusCode.ShouldBe(HttpStatusCode.OK, await wrong.Content.ReadAsStringAsync());
        (await ResultAsync(wrong)).ReleasedNudgeIds.ShouldBeEmpty("that session holds nothing");

        using var unknown = await host.PostAsync(Guid.NewGuid(), "no such pane", host.Token);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await f.ReloadAsync(stranded.Id)).AttemptState.ShouldBe(ExpectationAttemptState.Unconfirmed,
            "a release of another session leaves this one's hold alone");
        (await ReleasedCommentsAsync(f)).ShouldBeEmpty();
        await Should.ThrowAsync<ConflictException>(() => f.Harness.Queue.EnqueueAsync(
            f.SessionId, "operator send now", MessageSendMode.Now, CancellationToken.None));
    }

    [Test]
    public async Task C650_Repeated_release_is_idempotent()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        await using var host = await ReleaseHost.StartAsync(f);
        var task = await f.SubjectTaskAsync();
        var stranded = await StrandAsync(f, task);

        // Two at once, then a third later: exactly one of them releases, and it is audited once.
        var racing = await Task.WhenAll(
            host.PostAsync(f.SessionId, "first look", host.Token),
            host.PostAsync(f.SessionId, "second look", host.Token));
        var released = new List<Guid>();
        foreach (var response in racing)
        {
            using (response)
            {
                response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
                released.AddRange((await ResultAsync(response)).ReleasedNudgeIds);
            }
        }
        released.ShouldBe([stranded.Id], "one release wins; the other finds nothing to release");
        using var again = await host.PostAsync(f.SessionId, "third look", host.Token);
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ResultAsync(again)).ReleasedNudgeIds.ShouldBeEmpty();

        (await ReleasedCommentsAsync(f)).Count.ShouldBe(1);
        await using (var db = f.Db())
        {
            (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == task && e.Type == AgentTaskEventType.Check
                && e.Detail!.Contains(ExpectationHoldAudit.ReleasedMarkerPrefix))).ShouldBe(1);
        }
        (await f.ReloadAsync(stranded.Id)).AttemptState.ShouldBe(ExpectationAttemptState.Released);
        f.Harness.Adapter.Inputs.Count(i => i == stranded.Body).ShouldBe(1, "releasing types nothing");
    }

    [Test]
    public async Task C650_Release_racing_a_watchdog_send_never_releases_the_send_in_flight()
    {
        // A release that arrives while a watchdog prompt is being typed must wait for that send to
        // record its outcome. Otherwise it finds the attempt still Attempting, marks it Released, and
        // the confirmed receipt is lost.
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        await using var host = await ReleaseHost.StartAsync(f);
        var adapter = f.Harness.Adapter;
        adapter.ClaudeComposerChrome = true;
        var record = adapter.OnSubmitted!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        adapter.OnSubmitted = async submitted =>
        {
            entered.TrySetResult();
            await gate.Task;
            await record(submitted);
        };

        var nudge = await f.NudgeAsync(checkTaskId: await f.SubjectTaskAsync());
        var sending = f.DeliverAsync(nudge.Id);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        (await f.ReloadAsync(nudge.Id)).AttemptState.ShouldBe(ExpectationAttemptState.Attempting,
            "sanity: the Enter is in flight and the attempt is committed");

        var releasing = host.PostAsync(f.SessionId, "the pane looked clear", host.Token);
        await Task.Delay(200);
        releasing.IsCompleted.ShouldBeFalse("the release waits for the send that holds the session");

        gate.SetResult();
        (await sending).Outcome.ShouldBe(ExpectationSendOutcome.Confirmed);
        using var response = await releasing;
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await ResultAsync(response)).ReleasedNudgeIds.ShouldBeEmpty("the send it waited for was confirmed");

        var stored = await f.ReloadAsync(nudge.Id);
        stored.AttemptState.ShouldBe(ExpectationAttemptState.Confirmed, "the receipt survives the racing release");
        stored.ReceiptAt.ShouldNotBeNull();
        (await ReleasedCommentsAsync(f)).ShouldBeEmpty();
        adapter.Inputs.ShouldBe([nudge.Body, "\r"]);
        adapter.KillCount.ShouldBe(0);
    }

    /// <summary>A watchdog prompt whose every Enter was swallowed: Unconfirmed, standing in the composer, holding.</summary>
    private static async Task<ExpectationNudge> StrandAsync(ExpectationDeliveryFixture f, Guid checkTaskId)
    {
        f.Harness.Adapter.SwallowSubmits = 99;
        var nudge = await f.NudgeAsync(checkTaskId: checkTaskId);
        (await f.DeliverAsync(nudge.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Unconfirmed);
        (await f.ReloadAsync(nudge.Id)).AttemptState.ShouldBe(ExpectationAttemptState.Unconfirmed, "sanity: the hold stands");
        return nudge;
    }

    private static async Task<List<CardComment>> ReleasedCommentsAsync(ExpectationDeliveryFixture f)
    {
        await using var db = f.Db();
        return await db.CardComments.AsNoTracking()
            .Where(c => c.CardId == f.World.CardId && c.Body.Contains(ExpectationHoldAudit.ReleasedMarkerPrefix))
            .ToListAsync();
    }

    private static async Task<ExpectationHoldReleaseResult> ResultAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ExpectationHoldReleaseResult>(Json))!;

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>
    /// The route as production maps it, with production's exception and current-user middleware,
    /// over the fixture's queue service. The operator token file is this host's own.
    /// </summary>
    private sealed class ReleaseHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly string _directory;

        private ReleaseHost(WebApplication app, string directory, string token)
        {
            _app = app;
            _directory = directory;
            Token = token;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        /// <summary>The operator credential. Sent as a header only; never printed.</summary>
        public string Token { get; }

        public static async Task<ReleaseHost> StartAsync(ExpectationDeliveryFixture f)
        {
            var directory = Path.Combine(Path.GetTempPath(), "antiphon-c650-release-" + Guid.NewGuid().ToString("N"));
            var tokenPath = Path.Combine(directory, "operator-token");
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.Configure<PhoneHomeRunnerSettings>(o => o.OperatorTokenPath = tokenPath);
            builder.Services.AddSingleton(f.Harness.Queue);
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<ICurrentUser>(sp =>
                sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.Items["CurrentUser"] as ICurrentUser
                ?? throw new InvalidOperationException("CurrentUserMiddleware did not run."));
            var app = builder.Build();
            app.UseMiddleware<CurrentUserMiddleware>();
            app.UseMiddleware<ExceptionMiddleware>();
            app.MapGroup("/api/sessions").MapExpectationHoldRelease();
            await app.StartAsync();
            return new ReleaseHost(app, directory, OperatorTokenFile.ReadOrCreate(tokenPath));
        }

        /// <param name="proxied">CARD-0658's shape of a request through the public vhost: Host
        /// rewritten to localhost:17202, X-Forwarded-* naming a tailnet client.</param>
        public Task<HttpResponseMessage> PostAsync(Guid sessionId, string reason, string? token, bool proxied = false)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, ExpectationHoldAudit.ReleaseRoute(sessionId))
            {
                Content = JsonContent.Create(new ReleaseExpectationHoldRequest(reason), options: Json),
            };
            if (token is not null)
                request.Headers.TryAddWithoutValidation(OperatorTokenFile.Header, token);
            if (proxied)
            {
                request.Headers.Host = "localhost:17202";
                request.Headers.TryAddWithoutValidation("X-Forwarded-For", "100.64.0.7");
                request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "antiphon.desktop.codeperf.net");
            }
            return Client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // Best effort: a temp directory.
            }
        }
    }
}
