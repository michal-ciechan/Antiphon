using System.Net;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

[Category("Integration")]
public class CompactionContinuationWireTests
{
    [Test]
    public async Task Stop_request_round_trips_and_unsupported_observation_is_not_success()
    {
        var attempt = Guid.NewGuid();
        var accepted = new DateTime(2026, 9, 14, 22, 50, 7, DateTimeKind.Utc);
        var request = new CompactionContinuationStopRequest(
            attempt, accepted, "boundary-1", "cont-1", 10, "bind-1", 3, 4);
        var json = JsonSerializer.Serialize(request);
        var restored = JsonSerializer.Deserialize<CompactionContinuationStopRequest>(json);
        restored.ShouldNotBeNull();
        restored.AttemptId.ShouldBe(attempt);
        restored.NativeBoundaryIdentity.ShouldBe("boundary-1");
        restored.ThresholdMinutes.ShouldBe(10);

        var fake = new FakeSessionRunnerClient();
        var unseen = await fake.ObserveCompactionAsync(Guid.NewGuid(), CancellationToken.None);
        unseen.IsSuccessful.ShouldBeFalse();
        unseen.Status.ShouldBe(CompactionObservationStatuses.Unsupported);

        fake.CompactionObservation = new CompactionTailObservation(
            CompactionObservationStatuses.Success, true, 8, "bind-1", 3, 4, "boundary-1", "cont-1");
        fake.CompactionStopResult = new CompactionContinuationStopResult(
            Guid.NewGuid(), attempt, true, CompactionStopOutcomes.Exited, accepted);
        var observed = await fake.ObserveCompactionAsync(Guid.NewGuid(), CancellationToken.None);
        observed.IsSuccessful.ShouldBeTrue();
        var stopped = await fake.StopCompactionContinuationAsync(observed.TranscriptRevision == 3
            ? Guid.NewGuid() : Guid.Empty, request, CancellationToken.None);
        stopped.ConfirmsExit.ShouldBeTrue();
        stopped.Outcome.ShouldBe(CompactionStopOutcomes.Exited);
        fake.CompactionStops.Count.ShouldBe(1);
        CompactionContinuationStopCapability.Feature.ShouldBe("compactionContinuationStopV1");
    }

    // ---- the PRODUCTION client on the wire ----------------------------------------------------
    // Everything above rides FakeSessionRunnerClient, which cannot be wrong about a route, a verb
    // or the capability gate because it has none. These drive the real SessionRunnerHttpClient
    // through a StubHandler (the SessionRunnerGenerationWireTests shape), so a wrong path, a
    // wrong method, a dropped payload field or a lost capability check fails here rather than
    // only against a live runner.

    [Test]
    [Arguments("features-null")]
    [Arguments("features-without-token")]
    public async Task Missing_compaction_capability_sends_no_stop_request(string shape)
    {
        IReadOnlyList<string>? features = shape == "features-null" ? null : ["herdr-attach", "sessionGenerationV1"];
        var sessionId = Guid.NewGuid();
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/capabilities" => Json(Capabilities(features)),
            // Anything else would be the bug: a runner that cannot do this must never be asked.
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
        });

        var stop = await Client(handler).StopCompactionContinuationAsync(sessionId, Request(), CancellationToken.None);

        stop.ConfirmsExit.ShouldBeFalse();
        stop.Outcome.ShouldBe(CompactionStopOutcomes.Unsupported);
        stop.AcceptedStartedAt.ShouldBeNull();
        handler.Requests.Select(r => r.RequestUri!.AbsolutePath).ShouldBe(["/capabilities"]);

        handler.Requests.Clear();
        var observation = await Client(handler).ObserveCompactionAsync(sessionId, CancellationToken.None);

        observation.IsSuccessful.ShouldBeFalse();
        observation.Status.ShouldBe(CompactionObservationStatuses.Unsupported);
        handler.Requests.Select(r => r.RequestUri!.AbsolutePath).ShouldBe(["/capabilities"]);
    }

    [Test]
    public async Task Stop_and_observation_use_the_production_routes()
    {
        var sessionId = Guid.NewGuid();
        var attempt = Guid.NewGuid();
        var accepted = SessionGeneration.Normalize(new DateTime(2026, 9, 14, 22, 50, 7, DateTimeKind.Utc));
        var request = new CompactionContinuationStopRequest(
            attempt, accepted, "boundary-1", "cont-1", 10, "bind-1", 3, 4);
        var handler = new StubHandler(message => message.RequestUri!.AbsolutePath switch
        {
            "/capabilities" => Json(Capabilities([RunnerCapabilityFeatures.CompactionContinuationStopV1])),
            var path when path.EndsWith("/stop-compaction-continuation", StringComparison.Ordinal) =>
                Json(new CompactionContinuationStopResult(
                    sessionId, attempt, true, CompactionStopOutcomes.Exited, accepted)),
            var path when path.EndsWith("/compaction-observation", StringComparison.Ordinal) =>
                Json(new CompactionTailObservation(
                    CompactionObservationStatuses.Success, true, 80, "bind-1", 3, 4, "boundary-1", "cont-1")),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var client = Client(handler);

        var stop = await client.StopCompactionContinuationAsync(sessionId, request, CancellationToken.None);
        var observation = await client.ObserveCompactionAsync(sessionId, CancellationToken.None);

        stop.ConfirmsExit.ShouldBeTrue();
        stop.Outcome.ShouldBe(CompactionStopOutcomes.Exited);
        SessionGeneration.Equal(stop.AcceptedStartedAt, accepted).ShouldBeTrue();
        observation.IsSuccessful.ShouldBeTrue();
        observation.NativeBoundaryId.ShouldBe("boundary-1");
        observation.NativeContinuationId.ShouldBe("cont-1");

        var posted = handler.Requests.Single(r =>
            r.RequestUri!.AbsolutePath.EndsWith("/stop-compaction-continuation", StringComparison.Ordinal));
        posted.Method.ShouldBe(HttpMethod.Post);
        posted.RequestUri!.AbsolutePath.ShouldBe($"/sessions/{sessionId:D}/stop-compaction-continuation");
        var observed = handler.Requests.Single(r =>
            r.RequestUri!.AbsolutePath.EndsWith("/compaction-observation", StringComparison.Ordinal));
        observed.Method.ShouldBe(HttpMethod.Get);
        observed.RequestUri!.AbsolutePath.ShouldBe($"/sessions/{sessionId:D}/compaction-observation");

        // The whole conditional contract has to reach the runner: without the expected generation,
        // the two identities and the observed revisions it cannot decide anything conditionally.
        using var body = JsonDocument.Parse(await posted.Content!.ReadAsStringAsync());
        var sent = body.RootElement;
        sent.GetProperty("attemptId").GetGuid().ShouldBe(attempt);
        SessionGeneration.Equal(
            DateTime.Parse(sent.GetProperty("expectedAcceptedStartedAt").GetString()!), accepted).ShouldBeTrue();
        sent.GetProperty("nativeBoundaryIdentity").GetString().ShouldBe("boundary-1");
        sent.GetProperty("nativeContinuationIdentity").GetString().ShouldBe("cont-1");
        sent.GetProperty("thresholdMinutes").GetInt32().ShouldBe(10);
        sent.GetProperty("bindingIdentity").GetString().ShouldBe("bind-1");
        sent.GetProperty("transcriptRevision").GetInt64().ShouldBe(3);
        sent.GetProperty("outputRevision").GetInt64().ShouldBe(4);
    }

    [Test]
    [Arguments("unsupported")]
    [Arguments("refused")]
    public async Task Conditional_stop_never_falls_back(string shape)
    {
        var sessionId = Guid.NewGuid();
        var accepted = SessionGeneration.Normalize(new DateTime(2026, 9, 14, 22, 50, 7, DateTimeKind.Utc));
        IReadOnlyList<string>? features =
            shape == "unsupported" ? null : [RunnerCapabilityFeatures.CompactionContinuationStopV1];
        var handler = new StubHandler(message => message.RequestUri!.AbsolutePath switch
        {
            "/capabilities" => Json(Capabilities(features)),
            var path when path.EndsWith("/stop-compaction-continuation", StringComparison.Ordinal) =>
                Json(new CompactionContinuationStopResult(
                    sessionId, Guid.NewGuid(), false, CompactionStopOutcomes.Refused, accepted)),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        var stop = await Client(handler).StopCompactionContinuationAsync(sessionId, Request(), CancellationToken.None);

        stop.ConfirmsExit.ShouldBeFalse();
        stop.Outcome.ShouldBe(shape == "unsupported"
            ? CompactionStopOutcomes.Unsupported
            : CompactionStopOutcomes.Refused);
        // A refusal is a verdict, not a licence to reach for the blunt instrument.
        handler.Requests.ShouldNotContain(r =>
            r.RequestUri!.AbsolutePath.EndsWith("/kill", StringComparison.Ordinal)
            || r.RequestUri.AbsolutePath.EndsWith("/kill-generation", StringComparison.Ordinal)
            || r.RequestUri.AbsolutePath.EndsWith("/input", StringComparison.Ordinal)
            || r.RequestUri.AbsolutePath.Contains("conditional-input", StringComparison.Ordinal));
    }

    private static CompactionContinuationStopRequest Request() => new(
        Guid.NewGuid(),
        SessionGeneration.Normalize(new DateTime(2026, 9, 14, 22, 50, 7, DateTimeKind.Utc)),
        "boundary-1", "cont-1", 10, "bind-1", 3, 4);

    private static SessionRunnerHttpClient Client(StubHandler handler) =>
        new(new HttpClient(handler), new StubFactory(),
            Options.Create(new SessionRunnerSettings { BaseUrl = "http://runner.test" }));

    private static RunnerCapabilitiesDto Capabilities(IReadOnlyList<string>? features) =>
        new("InboxConhost", "inbox", "test", false, Features: features);

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

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(response(request));
        }
    }
}
