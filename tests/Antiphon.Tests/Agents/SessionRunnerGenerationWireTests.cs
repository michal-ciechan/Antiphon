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

    private static AgentLaunchSpec Spec(DateTime generation) =>
        new("fake", AgentKind.ClaudeCode, "cmd", [], new Dictionary<string, string>(), Path.GetTempPath(), 120, 30,
            AcceptedStartedAt: generation);

    private static SessionRunnerHttpClient Client(StubHandler handler) =>
        new(new HttpClient(handler), new StubFactory(),
            Options.Create(new SessionRunnerSettings { BaseUrl = "http://runner.test" }));

    private static RunnerCapabilitiesDto Capabilities(IReadOnlyList<string>? features) =>
        new("InboxConhost", "inbox", "test", false, Features: features);

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
