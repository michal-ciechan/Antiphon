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
