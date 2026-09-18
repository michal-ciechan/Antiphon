using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

[Category("Integration")]
public class SessionRunnerGenerationWireTests
{
    [Test]
    public async Task A_generation_bearing_launch_posts_acceptedStartedAt_and_maps_the_echo()
    {
        var sessionId = Guid.NewGuid();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        HttpRequestMessage? posted = null;
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/capabilities")
                return Json(Capabilities([RunnerCapabilityFeatures.SessionGenerationV1]));
            posted = request;
            return Json(new RunnerSessionDto(
                sessionId, 1, DateTime.UtcNow, "Running", null, "", 0, AcceptedStartedAt: generation));
        });
        var client = Client(handler);

        var launched = await client.StartAsync(sessionId, Spec(generation), CancellationToken.None);

        launched.AcceptedStartedAt.ShouldBe(generation);
        posted.ShouldNotBeNull();
        posted!.RequestUri!.AbsolutePath.ShouldBe("/sessions");
        var body = await posted.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var written = DateTime.Parse(doc.RootElement.GetProperty("acceptedStartedAt").GetString()!);
        SessionGeneration.Equal(written, generation).ShouldBeTrue();
    }

    [Test]
    public async Task A_launch_response_without_the_echoed_generation_is_refused()
    {
        var sessionId = Guid.NewGuid();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/capabilities" => Json(Capabilities([RunnerCapabilityFeatures.SessionGenerationV1])),
            "/sessions" => Json(new RunnerSessionDto(sessionId, 1, DateTime.UtcNow, "Running", null, "", 0)),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var client = Client(handler);

        var error = await Should.ThrowAsync<ConflictException>(() =>
            client.StartAsync(sessionId, Spec(generation), CancellationToken.None));
        error.Code.ShouldBe(SessionGeneration.NotEchoed);
        handler.Requests.Count(r => r.RequestUri!.AbsolutePath == "/sessions").ShouldBe(1);
    }

    [Test]
    [Arguments("features-null")]
    [Arguments("features-without-token")]
    public async Task A_runner_without_sessionGenerationV1_refuses_a_generation_bearing_launch_and_attach_before_any_POST(
        string shape)
    {
        IReadOnlyList<string>? features = shape == "features-null" ? null : ["herdr-attach"];
        var sessionId = Guid.NewGuid();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/capabilities" => Json(Capabilities(features)),
            _ => Json(new RunnerSessionDto(sessionId, 1, DateTime.UtcNow, "Running", null, "", 0)),
        });
        var client = Client(handler);

        await Should.ThrowAsync<RunnerCapabilityMismatchException>(() =>
            client.StartAsync(sessionId, Spec(generation), CancellationToken.None));
        handler.Requests.Select(r => r.RequestUri!.AbsolutePath).ShouldBe(["/capabilities"]);

        handler.Requests.Clear();
        var attachClient = Client(handler);
        await Should.ThrowAsync<RunnerCapabilityMismatchException>(() =>
            attachClient.AttachHerdrAsync(new HerdrAttachRequest(
                sessionId, "pane", "claude", "claude", 1, "none", AcceptedStartedAt: generation),
                CancellationToken.None));
        handler.Requests.Select(r => r.RequestUri!.AbsolutePath).ShouldBe(["/capabilities"]);
    }

    [Test]
    [Arguments("mismatch-200")]
    [Arguments("404")]
    public async Task KillGeneration_posts_the_expected_generation_and_never_falls_back(string shape)
    {
        var sessionId = Guid.NewGuid();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/capabilities")
                return Json(Capabilities([RunnerCapabilityFeatures.SessionGenerationV1]));
            if (request.RequestUri!.AbsolutePath.EndsWith("/kill-generation", StringComparison.Ordinal))
            {
                if (shape == "404")
                    return new HttpResponseMessage(HttpStatusCode.NotFound)
                    {
                        Content = new StringContent("""{"type":"not_found","detail":"missing"}""", Encoding.UTF8, "application/problem+json"),
                    };
                return Json(new RunnerKillGenerationResult(sessionId, false, KillGenerationOutcomes.Mismatch, generation));
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });
        var client = Client(handler);

        if (shape == "404")
        {
            await Should.ThrowAsync<RunnerProblemException>(() =>
                client.KillGenerationAsync(sessionId, generation, CancellationToken.None));
        }
        else
        {
            var result = await client.KillGenerationAsync(sessionId, generation, CancellationToken.None);
            result.Killed.ShouldBeFalse();
            result.Outcome.ShouldBe(KillGenerationOutcomes.Mismatch);
        }

        handler.Requests.ShouldNotContain(r => r.RequestUri!.AbsolutePath.EndsWith("/kill", StringComparison.Ordinal)
            && !r.RequestUri.AbsolutePath.EndsWith("/kill-generation", StringComparison.Ordinal));
        handler.Requests.ShouldContain(r => r.RequestUri!.AbsolutePath.EndsWith($"/sessions/{sessionId:D}/kill-generation", StringComparison.Ordinal));
    }

    [Test]
    public async Task C514_Conditional_transport_never_falls_back_to_raw_input()
    {
        var sessionId = Guid.NewGuid();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/capabilities" => Json(Capabilities(null)),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var client = Client(handler);
        var result = await client.SendConditionalInputAsync(
            sessionId,
            new RunnerConditionalInputRequest(generation, 1, "\u001b"),
            CancellationToken.None);
        result.Outcome.ShouldBe(ConditionalInputOutcomes.Unsupported);
        handler.Requests.ShouldNotContain(r => r.RequestUri!.AbsolutePath.EndsWith("/input", StringComparison.Ordinal)
            && !r.RequestUri.AbsolutePath.Contains("conditional-input", StringComparison.Ordinal));
    }

    [Test]
    public async Task C514_Lost_conditional_reply_is_unknown()
    {
        var sessionId = Guid.NewGuid();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        var handler = new StubHandler(_ => throw new HttpRequestException("lost"));
        var client = Client(handler);
        var result = await client.SendConditionalInputAsync(
            sessionId,
            new RunnerConditionalInputRequest(generation, 1, "\u001b"),
            CancellationToken.None);
        result.Outcome.ShouldBe(ConditionalInputOutcomes.Unknown);
    }

    // ---- CARD-0511 S1: fresh evidence per launch decision -------------------------------------

    /// <summary>
    /// V-511-1 / G-511-1. The 09-13 shape and the test the investigation named as missing: a
    /// refusal filled the snapshot with runner A, the operator rebuilt, and the very next launch
    /// still refused on the snapshot it had taken for the PREVIOUS decision. One client, two
    /// runners, two probes.
    /// </summary>
    [Test]
    public async Task C511_V1_Launch_after_runner_replacement_reprobes_and_launches()
    {
        var sessionId = Guid.NewGuid();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        var runner = OldRunner();
        var handler = Switchable(sessionId, generation, () => runner);
        var client = Client(handler);

        var refusal = await Should.ThrowAsync<RunnerCapabilityMismatchException>(() =>
            client.StartAsync(sessionId, Spec(generation), CancellationToken.None));
        refusal.Message.ShouldContain("built from aaaaaaa");
        refusal.RunnerIdentity.ShouldBe(RunnerIdentity.Describe(BuildA));
        Paths(handler).ShouldBe(["/capabilities"]);

        runner = NewRunner();
        var launched = await client.StartAsync(sessionId, Spec(generation), CancellationToken.None);

        launched.AcceptedStartedAt.ShouldBe(generation);
        Paths(handler).ShouldBe(["/capabilities", "/capabilities", "/sessions"]);
    }

    /// <summary>
    /// V-511-2 / G-511-2. The mirror hole: a stale POSITIVE snapshot would let a downgraded runner
    /// silently open a pty-host with no transcript (CARD-0160 / CARD-0112).
    /// </summary>
    [Test]
    public async Task C511_V2_Stale_positive_never_launches_onto_a_downgraded_runner()
    {
        var sessionId = Guid.NewGuid();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        var runner = NewRunner();
        var handler = Switchable(sessionId, generation, () => runner);
        var client = Client(handler);

        await client.StartAsync(sessionId, Spec(generation), CancellationToken.None);
        Paths(handler).ShouldBe(["/capabilities", "/sessions"]);

        runner = OldRunner();
        var refusal = await Should.ThrowAsync<RunnerCapabilityMismatchException>(() =>
            client.StartAsync(sessionId, Spec(generation), CancellationToken.None));

        refusal.Message.ShouldContain(RunnerCapabilityFeatures.SessionGenerationV1);
        refusal.Message.ShouldContain("built from aaaaaaa");
        refusal.Message.ShouldNotContain("bbbbbbb");
        refusal.RunnerIdentity.ShouldBe(RunnerIdentity.Describe(BuildA));
        Paths(handler).ShouldBe(["/capabilities", "/sessions", "/capabilities"]);
    }

    /// <summary>
    /// V-511-3 / G-511-3. "Nobody answered" is not "the runner is stale": it classifies as
    /// Infrastructure and is paced by the ordinary ladder, never by a runner-build hold.
    /// </summary>
    [Test]
    [Arguments("refused")]
    [Arguments("timeout")]
    [Arguments("500")]
    [Arguments("malformed")]
    public async Task C511_V3_Unreachable_probe_on_a_gated_launch_is_RunnerUnreachable(string shape)
    {
        var sessionId = Guid.NewGuid();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath != "/capabilities")
                return Json(new RunnerSessionDto(sessionId, 1, DateTime.UtcNow, "Running", null, "", 0, AcceptedStartedAt: generation));
            return shape switch
            {
                "refused" => throw new HttpRequestException(
                    "refused", new SocketException((int)SocketError.ConnectionRefused)),
                "timeout" => throw new TaskCanceledException(),
                "500" => new HttpResponseMessage(HttpStatusCode.InternalServerError),
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("not json", Encoding.UTF8, "text/plain"),
                },
            };
        });
        var client = Client(handler);

        var error = await Should.ThrowAsync<RunnerUnreachableException>(() =>
            client.StartAsync(sessionId, Spec(generation), CancellationToken.None));
        error.InnerException.ShouldNotBeNull();
        Paths(handler).ShouldNotContain("/sessions");
        new RestartFailurePolicy().Classify(error).ShouldBe(RestartFailureKind.Infrastructure);

        var attachError = await Should.ThrowAsync<RunnerUnreachableException>(() =>
            client.AttachHerdrAsync(new HerdrAttachRequest(
                sessionId, "pane", "claude", "claude", 1, "none", AcceptedStartedAt: generation),
                CancellationToken.None));
        attachError.InnerException.ShouldNotBeNull();
        Paths(handler).ShouldNotContain("/sessions/attach");
    }

    /// <summary>
    /// V-511-3b. A runner that predates /capabilities answers 404: no evidence, the SAME refusal
    /// shape as today (a mismatch naming no build), never the unreachable refusal.
    /// </summary>
    [Test]
    public async Task C511_V3b_Older_runner_without_a_capabilities_endpoint_is_a_mismatch_not_unreachable()
    {
        var sessionId = Guid.NewGuid();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath == "/capabilities"
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : Json(new RunnerSessionDto(sessionId, 1, DateTime.UtcNow, "Running", null, "", 0, AcceptedStartedAt: generation)));
        var client = Client(handler);

        var error = await Should.ThrowAsync<RunnerCapabilityMismatchException>(() =>
            client.StartAsync(sessionId, Spec(generation), CancellationToken.None));

        error.Message.ShouldContain($"does not advertise {RunnerCapabilityFeatures.SessionGenerationV1}");
        error.Message.ShouldNotContain("built from");
        error.RunnerIdentity.ShouldBe("unknown");
        Paths(handler).ShouldNotContain("/sessions");
    }

    /// <summary>
    /// V-511-4 / G-511-4. An unreachable probe only refuses a launch that NEEDS positive evidence.
    /// A launch with no gate (or only the transcript gate, whose null answer means "launch") still
    /// reaches the POST, which then fails or succeeds on its own.
    /// </summary>
    [Test]
    [Arguments("claude")]
    [Arguments("codex")]
    public async Task C511_V4_Unreachable_probe_on_an_ungated_launch_proceeds(string kind)
    {
        var sessionId = Guid.NewGuid();
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath == "/capabilities"
            ? throw new HttpRequestException("refused")
            : Json(new RunnerSessionDto(sessionId, null, DateTime.UtcNow, "Running", null, "", 0)));
        var client = Client(handler);
        var spec = kind == "claude"
            ? new AgentLaunchSpec("fake", AgentKind.ClaudeCode, "cmd", [], new Dictionary<string, string>(), Path.GetTempPath(), 120, 30)
            : new AgentLaunchSpec("fake", AgentKind.Codex, "codex", [], new Dictionary<string, string>(), Path.GetTempPath(), 120, 30);

        var launched = await client.StartAsync(sessionId, spec, CancellationToken.None);

        launched.SessionId.ShouldBe(sessionId);
        Paths(handler).ShouldBe(["/capabilities", "/sessions"]);
    }

    /// <summary>
    /// V-511-5 / G-511-6, G-511-7. Attach and the herdr-backend gate are launch decisions too:
    /// each takes its own probe and answers from the runner of the moment.
    /// </summary>
    [Test]
    public async Task C511_V5_Attach_and_backend_gate_use_the_fresh_probe()
    {
        var sessionId = Guid.NewGuid();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        var runner = NewRunner();
        var handler = Switchable(sessionId, generation, () => runner);
        var client = Client(handler);
        var attach = new HerdrAttachRequest(sessionId, "pane", "claude", "claude", 1, "none", AcceptedStartedAt: generation);

        await client.StartAsync(sessionId, Spec(generation), CancellationToken.None);
        handler.Requests.Count.ShouldBe(2);

        runner = OldRunner();
        var backendMismatch = await client.GetSessionBackendCapabilityMismatchAsync(CancellationToken.None);
        backendMismatch.ShouldNotBeNull();
        backendMismatch!.ShouldContain("SessionBackends=pty-host");
        backendMismatch.ShouldContain("built from aaaaaaa");
        handler.Requests.Count.ShouldBe(3);

        var refusal = await Should.ThrowAsync<RunnerCapabilityMismatchException>(() =>
            client.AttachHerdrAsync(attach, CancellationToken.None));
        refusal.Message.ShouldContain("aaaaaaa");
        handler.Requests.Count.ShouldBe(4);
        Paths(handler).ShouldNotContain("/sessions/attach");

        runner = NewRunner();
        (await client.GetSessionBackendCapabilityMismatchAsync(CancellationToken.None)).ShouldBeNull();
        handler.Requests.Count.ShouldBe(5);

        await client.AttachHerdrAsync(attach, CancellationToken.None);
        handler.Requests.Count.ShouldBe(7);
        Paths(handler).TakeLast(2).ShouldBe(["/capabilities", "/sessions/attach"]);
    }

    /// <summary>
    /// V-511-5b / G-511-8. A decision probe leaves the watchdog's snapshot no older than the last
    /// launch, so the dispatcher never reads a runner two rebuilds behind.
    /// </summary>
    [Test]
    public async Task C511_V5b_A_decision_probe_refreshes_the_watchdog_snapshot()
    {
        var sessionId = Guid.NewGuid();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        var runner = OldRunner();
        var handler = Switchable(sessionId, generation, () => runner);
        var client = Client(handler);

        var stale = await client.GetTranscriptCapabilityMismatchAsync(AgentKind.Codex, CancellationToken.None);
        stale.ShouldNotBeNull();
        stale!.Message.ShouldContain("aaaaaaa");

        runner = NewRunner();
        await client.StartAsync(sessionId, Spec(generation), CancellationToken.None);

        (await client.GetTranscriptCapabilityMismatchAsync(AgentKind.Codex, CancellationToken.None)).ShouldBeNull();
    }

    internal static readonly RunnerBuildDto BuildA = new(
        $"1.0.0+{new string('a', 40)}", new string('a', 40), DateTime.UnixEpoch,
        new DateTime(2026, 9, 13, 9, 0, 0, DateTimeKind.Utc));

    internal static readonly RunnerBuildDto BuildB = new(
        $"1.0.0+{new string('b', 40)}", new string('b', 40), DateTime.UnixEpoch,
        new DateTime(2026, 9, 13, 15, 50, 0, DateTimeKind.Utc));

    private static RunnerCapabilitiesDto OldRunner() => Capabilities(
        [RunnerCapabilityFeatures.HerdrAttach], BuildA,
        backends: [SessionBackends.PtyHost], transcripts: [TranscriptFormats.Claude]);

    private static RunnerCapabilitiesDto NewRunner() => Capabilities(
        [RunnerCapabilityFeatures.HerdrAttach, RunnerCapabilityFeatures.SessionGenerationV1,
            RunnerCapabilityFeatures.HerdrNamedTabPlacement],
        BuildB,
        backends: [SessionBackends.PtyHost, SessionBackends.Herdr],
        transcripts: [TranscriptFormats.Claude, TranscriptFormats.Codex, TranscriptFormats.Grok]);

    /// <summary>One handler, one client, a runner that can be swapped between calls.</summary>
    private static StubHandler Switchable(Guid sessionId, DateTime generation, Func<RunnerCapabilitiesDto> runner) =>
        new(request => request.RequestUri!.AbsolutePath switch
        {
            "/capabilities" => Json(runner()),
            "/sessions" or "/sessions/attach" => Json(new RunnerSessionDto(
                sessionId, 1, DateTime.UtcNow, "Running", null, "", 0, AcceptedStartedAt: generation)),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

    private static List<string> Paths(StubHandler handler) =>
        handler.Requests.Select(r => r.RequestUri!.AbsolutePath).ToList();

    private static AgentLaunchSpec Spec(DateTime generation) =>
        new("fake", AgentKind.ClaudeCode, "cmd", [], new Dictionary<string, string>(), Path.GetTempPath(), 120, 30,
            AcceptedStartedAt: generation);

    private static SessionRunnerHttpClient Client(StubHandler handler) =>
        new(new HttpClient(handler), new StubFactory(),
            Options.Create(new SessionRunnerSettings { BaseUrl = "http://runner.test" }));

    private static RunnerCapabilitiesDto Capabilities(IReadOnlyList<string>? features) =>
        new("InboxConhost", "inbox", "test", false, Features: features);

    private static RunnerCapabilitiesDto Capabilities(
        IReadOnlyList<string>? features, RunnerBuildDto? build,
        IReadOnlyList<string>? backends = null, IReadOnlyList<string>? transcripts = null) =>
        new("InboxConhost", "inbox", "test", false, transcripts, build, backends, Features: features);

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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(response(request));
        }
    }
}
