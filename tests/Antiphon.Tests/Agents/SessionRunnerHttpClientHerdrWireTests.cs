using System.Net;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
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
