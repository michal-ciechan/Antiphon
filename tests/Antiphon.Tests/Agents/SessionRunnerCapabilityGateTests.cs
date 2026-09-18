using System.Net;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

[Category("Integration")]
public class SessionRunnerCapabilityGateTests
{
    [Test]
    public async Task Explicitly_missing_transcript_format_refuses_launch_with_restart_fix()
    {
        var handler = new StubHandler(request =>
        {
            request.RequestUri!.AbsolutePath.ShouldBe("/capabilities");
            var capabilities = new RunnerCapabilitiesDto(
                "InboxConhost", "inbox", "test", false,
                [TranscriptFormats.Claude, TranscriptFormats.Grok],
                new RunnerBuildDto("1.0.0+0123456789012345678901234567890123456789",
                    "0123456789012345678901234567890123456789", DateTime.UnixEpoch, DateTime.UnixEpoch));
            return Json(capabilities);
        });
        var client = new SessionRunnerHttpClient(
            new HttpClient(handler), new StubFactory(),
            Options.Create(new SessionRunnerSettings { BaseUrl = "http://runner.test" }));

        var error = await Should.ThrowAsync<RunnerCapabilityMismatchException>(() => client.StartAsync(
            Guid.NewGuid(), new AgentLaunchSpec("codex", AgentKind.Codex, "codex", [], new Dictionary<string, string>(), Path.GetTempPath(), 120, 30),
            CancellationToken.None));

        error.Message.ShouldContain("cannot tail a 'codex' transcript");
        error.Message.ShouldContain("restart-session-runner.ps1");
        handler.Requests.Count.ShouldBe(1, "a refused launch must never POST /sessions");
    }

    [Test]
    public async Task Absent_capability_field_is_no_evidence_and_launches_as_before()
    {
        var sessionId = Guid.NewGuid();
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/capabilities" => Json(new RunnerCapabilitiesDto("InboxConhost", "inbox", "old runner", false)),
            "/sessions" => Json(new RunnerSessionDto(sessionId, null, DateTime.UtcNow, "Running", null, "", 0)),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var client = new SessionRunnerHttpClient(
            new HttpClient(handler), new StubFactory(),
            Options.Create(new SessionRunnerSettings { BaseUrl = "http://runner.test" }));

        var launched = await client.StartAsync(
            sessionId, new AgentLaunchSpec("codex", AgentKind.Codex, "codex", [], new Dictionary<string, string>(), Path.GetTempPath(), 120, 30),
            CancellationToken.None);

        launched.SessionId.ShouldBe(sessionId);
        handler.Requests.Select(request => request.RequestUri!.AbsolutePath).ShouldBe(["/capabilities", "/sessions"]);
    }

    // ---- CARD-0511 S2: the watchdog cache keeps its last good answer ---------------------------

    /// <summary>
    /// V-511-6 / G-511-9, G-511-10. A failed probe is "I could not find out", not "no
    /// capabilities": it must never overwrite a good snapshot, and it must not pin that miss for
    /// the whole TTL (_capabilitiesProbedAt is stamped BEFORE the probe runs).
    /// </summary>
    [Test]
    public async Task C511_V6_Failed_probe_keeps_the_last_good_snapshot()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 13, 15, 0, 0, TimeSpan.Zero));
        var answer = true;
        var handler = new StubHandler(_ => answer
            ? Json(OldRunner())
            : throw new HttpRequestException("runner restarting"));
        var client = Client(handler, clock);

        var first = await client.GetTranscriptCapabilityMismatchAsync(AgentKind.Codex, CancellationToken.None);
        first.ShouldNotBeNull();
        first!.Message.ShouldContain("built from aaaaaaa");
        handler.Requests.Count.ShouldBe(1);

        answer = false;
        clock.Advance(TimeSpan.FromMinutes(6));
        var second = await client.GetTranscriptCapabilityMismatchAsync(AgentKind.Codex, CancellationToken.None);
        handler.Requests.Count.ShouldBe(2, "a stale snapshot starts a refresh probe");
        second.ShouldNotBeNull("the failed probe must keep the last good answer, not serve null");
        second!.Message.ShouldContain("built from aaaaaaa");

        // No clock advance: the failed probe marked the cache stale, so this call probes again
        // rather than serving the miss for the rest of the TTL.
        var third = await client.GetTranscriptCapabilityMismatchAsync(AgentKind.Codex, CancellationToken.None);
        handler.Requests.Count.ShouldBe(3);
        third.ShouldNotBeNull();
    }

    /// <summary>V-511-7 / G-511-10. A failed FIRST probe re-probes on the next call.</summary>
    [Test]
    public async Task C511_V7_Failed_first_probe_reprobes_on_the_next_call()
    {
        var calls = 0;
        var handler = new StubHandler(_ => ++calls == 1
            ? throw new HttpRequestException("runner restarting")
            : Json(OldRunner()));
        var client = Client(handler);

        (await client.GetTranscriptCapabilityMismatchAsync(AgentKind.Codex, CancellationToken.None)).ShouldBeNull();

        var second = await client.GetTranscriptCapabilityMismatchAsync(AgentKind.Codex, CancellationToken.None);
        handler.Requests.Count.ShouldBe(2);
        second.ShouldNotBeNull();
        second!.Message.ShouldContain("aaaaaaa");
    }

    private static RunnerCapabilitiesDto OldRunner() => new(
        "InboxConhost", "inbox", "test", false,
        [TranscriptFormats.Claude],
        new RunnerBuildDto($"1.0.0+{new string('a', 40)}", new string('a', 40), DateTime.UnixEpoch,
            new DateTime(2026, 9, 13, 9, 0, 0, DateTimeKind.Utc)),
        [SessionBackends.PtyHost]);

    private static SessionRunnerHttpClient Client(StubHandler handler, TimeProvider? clock = null) =>
        new(new HttpClient(handler), new StubFactory(),
            Options.Create(new SessionRunnerSettings { BaseUrl = "http://runner.test" }), null, clock);

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json"),
    };

    private sealed class StubFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(response(request));
        }
    }
}
