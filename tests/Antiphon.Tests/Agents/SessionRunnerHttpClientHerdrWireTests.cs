using System.Net;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-0187 S2: <see cref="SessionRunnerHttpClient"/> serializes <c>Herdr.AgentKind</c> and the
/// CARD-0112 transcript-format contract (Claude = null, Grok = "grok") onto the launch POST.
/// </summary>
[Category("Integration")]
public class SessionRunnerHttpClientHerdrWireTests
{
    [Test]
    public async Task Grok_spec_on_herdr_posts_agent_kind_grok_and_transcript_format_grok()
    {
        var launched = await CaptureLaunchAsync(
            new AgentLaunchSpec(
                "grok",
                AgentKind.Grok,
                "grok.exe",
                [],
                new Dictionary<string, string>(),
                Path.GetTempPath(),
                120,
                30,
                Backend: SessionBackend.Herdr,
                Herdr: new HerdrLaunchOptions("none", "Antiphon", null, "g", AgentKind: HerdrAgentKinds.Grok)));

        launched.Herdr.ShouldNotBeNull();
        launched.Herdr.AgentKind.ShouldBe(HerdrAgentKinds.Grok);
        launched.TranscriptFormat.ShouldBe(TranscriptFormats.Grok);
        launched.Backend.ShouldBe(SessionBackends.Herdr);
    }

    [Test]
    public async Task AgentSlug_appears_on_the_POST_body()
    {
        var launched = await CaptureLaunchAsync(
            new AgentLaunchSpec(
                "grok",
                AgentKind.Grok,
                "grok.exe",
                [],
                new Dictionary<string, string>(),
                Path.GetTempPath(),
                120,
                30,
                Backend: SessionBackend.Herdr,
                Herdr: new HerdrLaunchOptions(
                    "none", "Antiphon", null, "PM-Orchestrator-Grok",
                    AgentKind: HerdrAgentKinds.Grok,
                    AgentSlug: "pm-orchestrator-grok")));

        launched.Herdr.ShouldNotBeNull();
        launched.Herdr.AgentSlug.ShouldBe("pm-orchestrator-grok");
        launched.Herdr.PaneTitle.ShouldBe("PM-Orchestrator-Grok");
    }

    [Test]
    public async Task ReusePaneOfSessionId_appears_on_the_POST_body()
    {
        var previous = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var launched = await CaptureLaunchAsync(
            new AgentLaunchSpec(
                "grok",
                AgentKind.Grok,
                "grok.exe",
                [],
                new Dictionary<string, string>(),
                Path.GetTempPath(),
                120,
                30,
                Backend: SessionBackend.Herdr,
                Herdr: new HerdrLaunchOptions(
                    "none", "Antiphon", null, "g",
                    AgentKind: HerdrAgentKinds.Grok,
                    ReusePaneOfSessionId: previous)));

        launched.Herdr.ShouldNotBeNull();
        launched.Herdr.ReusePaneOfSessionId.ShouldBe(previous);
    }

    [Test]
    public void Old_launch_body_without_reusePaneOfSessionId_deserialises_null()
    {
        const string json = """
            {"sessionId":"00000000-0000-0000-0000-000000000001","exe":"grok.exe","args":[],"env":{},"cwd":"c:\\\\tmp","cols":120,"rows":30,"herdr":{"workspaceKey":"none","workspaceLabel":"Antiphon","workspaceCwd":null,"paneTitle":"g","agentKind":"grok","agentSlug":"g"}}
            """;

        var launched = JsonSerializer.Deserialize<RunnerLaunchRequest>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        launched.ShouldNotBeNull();
        launched.Herdr.ShouldNotBeNull();
        launched.Herdr.ReusePaneOfSessionId.ShouldBeNull();
        launched.Herdr.AgentSlug.ShouldBe("g");
    }

    [Test]
    public void Old_launch_body_without_agentSlug_deserialises_AgentSlug_null()
    {
        const string json = """
            {"sessionId":"00000000-0000-0000-0000-000000000001","exe":"grok.exe","args":[],"env":{},"cwd":"c:\\\\tmp","cols":120,"rows":30,"herdr":{"workspaceKey":"none","workspaceLabel":"Antiphon","workspaceCwd":null,"paneTitle":"g","agentKind":"grok"}}
            """;

        var launched = JsonSerializer.Deserialize<RunnerLaunchRequest>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        launched.ShouldNotBeNull();
        launched.Herdr.ShouldNotBeNull();
        launched.Herdr.AgentSlug.ShouldBeNull();
        launched.Herdr.AgentKind.ShouldBe(HerdrAgentKinds.Grok);
    }

    [Test]
    public void HerdrOrigin_round_trips_on_the_wire_and_absent_is_null()
    {
        var withOrigin = new RunnerSessionDto(
            Guid.NewGuid(), 1, DateTime.UtcNow, "Running", null, "", 0, HerdrOrigin: HerdrPaneOrigins.Attached);
        var roundTrip = JsonSerializer.Deserialize<RunnerSessionDto>(
            JsonSerializer.Serialize(withOrigin, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        roundTrip.ShouldNotBeNull();
        roundTrip!.HerdrOrigin.ShouldBe(HerdrPaneOrigins.Attached);

        var old = JsonSerializer.Deserialize<RunnerSessionDto>(
            """{"sessionId":"00000000-0000-0000-0000-000000000001","pid":1,"startedAt":"2026-01-01T00:00:00Z","status":"Running","exitCode":null,"exitReason":"","lastSequence":0}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        old.ShouldNotBeNull();
        old!.HerdrOrigin.ShouldBeNull();
    }

    [Test]
    public async Task Problem_details_409_maps_to_conflict_with_the_runner_code()
    {
        var handler = new CapturingHandler(_ => Task.FromResult(Problem(
            409, HerdrProblemTypes.PaneBound, "pane w2:p3 is bound to session aaaa")));
        var client = new SessionRunnerHttpClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://runner.test/") },
            new StubFactory(),
            Options.Create(new SessionRunnerSettings { BaseUrl = "http://runner.test" }));

        var ex = await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(() =>
            client.AttachHerdrAsync(
                new HerdrAttachRequest(
                    Guid.NewGuid(), "w2:p3", HerdrAgentKinds.Grok, TranscriptFormats.Grok,
                    1, "none"),
                CancellationToken.None));
        ex.Code.ShouldBe(HerdrProblemTypes.PaneBound);
        ex.Message.ShouldContain("w2:p3");
    }

    [Test]
    public async Task Launch_409_problem_maps_to_conflict_carrying_the_runner_detail()
    {
        // CARD-0341: the gkp gate's refusal (and CARD-0224's pane_occupied) must reach
        // FailureReason as the runner's own words, not "status code does not indicate success".
        const string detail = "refusing to type a gkp Grok launch: the launch env carries no GROK_BASE_URL";
        var handler = new CapturingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/capabilities")
            {
                return Task.FromResult(Json(new RunnerCapabilitiesDto(
                    "InboxConhost",
                    "inbox",
                    "test",
                    false,
                    TranscriptFormats: [TranscriptFormats.Claude, TranscriptFormats.Grok, TranscriptFormats.Codex],
                    SessionBackends: [SessionBackends.PtyHost, SessionBackends.Herdr])));
            }

            return Task.FromResult(Problem(409, HerdrProblemTypes.GkpEnvMissing, detail));
        });
        var client = new SessionRunnerHttpClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://runner.test/") },
            new StubFactory(),
            Options.Create(new SessionRunnerSettings { BaseUrl = "http://runner.test" }));

        var ex = await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(() =>
            client.StartAsync(
                Guid.NewGuid(),
                new AgentLaunchSpec(
                    "grok-gkp-project",
                    AgentKind.Grok,
                    "pwsh.exe",
                    ["-File", @"C:\x\gkp.ps1"],
                    new Dictionary<string, string>(),
                    Path.GetTempPath(),
                    120,
                    30,
                    Backend: SessionBackend.Herdr,
                    Herdr: new HerdrLaunchOptions("none", "Antiphon", null, "g", AgentKind: HerdrAgentKinds.Grok)),
                CancellationToken.None));
        ex.Code.ShouldBe(HerdrProblemTypes.GkpEnvMissing);
        ex.Message.ShouldBe(detail);
    }

    [Test]
    public async Task Launch_409_grok_native_session_missing_maps_to_conflict_with_the_code()
    {
        const string detail = "refusing to type `--resume` for session: no grok session directory";
        var handler = new CapturingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/capabilities")
            {
                return Task.FromResult(Json(new RunnerCapabilitiesDto(
                    "InboxConhost",
                    "inbox",
                    "test",
                    false,
                    TranscriptFormats: [TranscriptFormats.Claude, TranscriptFormats.Grok, TranscriptFormats.Codex],
                    SessionBackends: [SessionBackends.PtyHost, SessionBackends.Herdr])));
            }

            return Task.FromResult(Problem(409, HerdrProblemTypes.GrokNativeSessionMissing, detail));
        });
        var client = new SessionRunnerHttpClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://runner.test/") },
            new StubFactory(),
            Options.Create(new SessionRunnerSettings { BaseUrl = "http://runner.test" }));

        var ex = await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(() =>
            client.StartAsync(
                Guid.NewGuid(),
                new AgentLaunchSpec(
                    "grok",
                    AgentKind.Grok,
                    "grok.exe",
                    ["--resume", Guid.NewGuid().ToString("D")],
                    new Dictionary<string, string>(),
                    Path.GetTempPath(),
                    120,
                    30,
                    Backend: SessionBackend.Herdr,
                    Herdr: new HerdrLaunchOptions("none", "Antiphon", null, "g", AgentKind: HerdrAgentKinds.Grok)),
                CancellationToken.None));
        ex.Code.ShouldBe(HerdrProblemTypes.GrokNativeSessionMissing);
        ex.Message.ShouldBe(detail);
    }

    [Test]
    public async Task Problem_details_404_maps_to_runner_problem()
    {
        var handler = new CapturingHandler(_ => Task.FromResult(Problem(
            404, HerdrProblemTypes.PaneNotFound, "pane missing")));
        var client = new SessionRunnerHttpClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://runner.test/") },
            new StubFactory(),
            Options.Create(new SessionRunnerSettings { BaseUrl = "http://runner.test" }));

        var ex = await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.RunnerProblemException>(() =>
            client.InspectHerdrPaneAsync("w-missing:p1", CancellationToken.None));
        ex.StatusCode.ShouldBe(404);
        ex.Code.ShouldBe(HerdrProblemTypes.PaneNotFound);
    }

    [Test]
    public async Task HerdrLaunchDetectTimeout_exit_reason_maps_to_the_enum_member()
    {
        var sessionId = Guid.NewGuid();
        var handler = new CapturingHandler(_ => Task.FromResult(Json(new RunnerSessionDto(
            sessionId,
            Pid: null,
            StartedAt: DateTime.UtcNow,
            Status: "Exited",
            ExitCode: null,
            ExitReason: HerdrExitReasons.LaunchDetectTimeout,
            LastSequence: 0))));
        var client = new SessionRunnerHttpClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://runner.test/") },
            new StubFactory(),
            Options.Create(new SessionRunnerSettings { BaseUrl = "http://runner.test" }));

        var dto = await client.GetAsync(sessionId, CancellationToken.None);
        dto.ExitReason.ShouldBe(AgentExitReason.HerdrLaunchDetectTimeout);
        dto.ExitReason.ShouldNotBe(AgentExitReason.HerdrPaneLeftOpen);
        dto.ExitReason.ShouldNotBe(AgentExitReason.Unknown);
    }

    [Test]
    public async Task Claude_spec_on_herdr_posts_agent_kind_claude_and_null_transcript_format()
    {
        var launched = await CaptureLaunchAsync(
            new AgentLaunchSpec(
                "claude",
                AgentKind.ClaudeCode,
                "claude.exe",
                [],
                new Dictionary<string, string>(),
                Path.GetTempPath(),
                120,
                30,
                Backend: SessionBackend.Herdr,
                Herdr: new HerdrLaunchOptions("none", "Antiphon", null, "c", AgentKind: HerdrAgentKinds.Claude)));

        launched.Herdr.ShouldNotBeNull();
        launched.Herdr.AgentKind.ShouldBe(HerdrAgentKinds.Claude);
        launched.TranscriptFormat.ShouldBeNull("CARD-0112: Claude is the pre-Grok default");
        launched.Backend.ShouldBe(SessionBackends.Herdr);
    }

    private static async Task<RunnerLaunchRequest> CaptureLaunchAsync(AgentLaunchSpec spec)
    {
        var sessionId = Guid.NewGuid();
        string? posted = null;
        var handler = new CapturingHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/capabilities")
            {
                return Json(new RunnerCapabilitiesDto(
                    "InboxConhost",
                    "inbox",
                    "test",
                    false,
                    TranscriptFormats: [TranscriptFormats.Claude, TranscriptFormats.Grok, TranscriptFormats.Codex],
                    SessionBackends: [SessionBackends.PtyHost, SessionBackends.Herdr]));
            }

            if (request.RequestUri.AbsolutePath == "/sessions")
            {
                posted = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync();
                return Json(new RunnerSessionDto(sessionId, null, DateTime.UtcNow, "Running", null, "", 0));
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = new SessionRunnerHttpClient(
            new HttpClient(handler),
            new StubFactory(),
            Options.Create(new SessionRunnerSettings { BaseUrl = "http://runner.test" }));

        await client.StartAsync(sessionId, spec, CancellationToken.None);
        posted.ShouldNotBeNull("StartAsync must POST /sessions");
        return JsonSerializer.Deserialize<RunnerLaunchRequest>(
            posted,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("launch body did not deserialize");
    }

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Encoding.UTF8,
            "application/json"),
    };

    private static HttpResponseMessage Problem(int status, string type, string detail) =>
        new((HttpStatusCode)status)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { type, title = type, status, detail }),
                Encoding.UTF8,
                "application/problem+json"),
        };

    private sealed class StubFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class CapturingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => response(request);
    }
}
