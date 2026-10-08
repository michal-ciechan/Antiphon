using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-1153 S3: the production <c>SessionRunnerHttpClient</c> over a <c>StubHandler</c>
/// (the <c>SessionRunnerGenerationWireTests</c> shape), FakeTimeProvider, synthetic key.
/// Design: V-12, V-13. Shapes come from <see cref="AbsenceCertificateShape"/>; the pristine
/// control is signed by Code with the production signer.
/// </summary>
[Category("Integration")]
public class SessionRunnerAbsenceEvidenceClientTests
{
    private static readonly DateTime Generation = new DateTime(2026, 10, 8, 7, 8, 9, DateTimeKind.Utc).AddTicks(4560);

    /// <summary>
    /// V-12. Every shape yields the unsupported/unknown application result, never a validated
    /// proof, and exactly one POST was sent. ordinary-transcript is a complete empty transcript
    /// body served on the certify route; it is not a certificate.
    /// </summary>
    [Test]
    [Arguments("404")]
    [Arguments("501")]
    [Arguments("empty-200")]
    [Arguments("malformed")]
    [Arguments("ordinary-transcript")]
    [Arguments("missing-presence-field")]
    [Arguments("incomplete")]
    [Arguments("wrong-ID")]
    [Arguments("wrong-generation")]
    [Arguments("wrong-store")]
    [Arguments("wrong-nonce")]
    [Arguments("unknown-version")]
    [Arguments("unsigned")]
    [Arguments("bad-MAC")]
    public async Task C1153_Rejects_noncertificate_wire_shapes(string shape)
    {
        using var world = new ClientWorld();
        var control = await world.CertifyAsync(_ => ClientWorld.Answer.Pristine);
        control.Kind.ShouldBe(SessionRunnerAbsenceEvidenceKind.Proven, "pristine signed control must admit: " + control.Reason);
        world.Posts.Count.ShouldBe(1);
        world.Posts.Clear();

        var result = await world.CertifyAsync(request => shape switch
        {
            "404" => ClientWorld.Answer.Status(HttpStatusCode.NotFound, "{\"type\":\"absence_evidence_not_prepared\"}"),
            "501" => ClientWorld.Answer.Status(HttpStatusCode.NotImplemented, ""),
            "empty-200" => ClientWorld.Answer.Raw(""),
            "malformed" => ClientWorld.Answer.Raw("{\"version\":1,"),
            "ordinary-transcript" => ClientWorld.Answer.Raw(JsonSerializer.Serialize(
                new RunnerTranscriptDto(request.SessionId, [], 0, true, Generation),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))),
            "missing-presence-field" => ClientWorld.Answer.Shape(s => s.Remove("processPresent")),
            "incomplete" => ClientWorld.Answer.Shape(s => s["complete"] = false),
            "wrong-ID" => ClientWorld.Answer.Shape(s => s["sessionId"] = Guid.NewGuid().ToString("D")),
            "wrong-generation" => ClientWorld.Answer.Shape(s => s["acceptedStartedAt"] =
                SessionGeneration.Normalize(Generation).AddTicks(SessionGeneration.MicrosecondTicks).ToString("O")),
            "wrong-store" => ClientWorld.Answer.Shape(s => s["runnerStoreId"] = Guid.NewGuid().ToString("D")),
            "wrong-nonce" => ClientWorld.Answer.Shape(s => s["requestNonce"] = RunnerAbsenceEvidence.NewNonce()),
            "unknown-version" => ClientWorld.Answer.Shape(s => s["version"] = 2),
            "unsigned" => ClientWorld.Answer.Pristine with { Sign = false },
            "bad-MAC" => ClientWorld.Answer.Pristine with { ForeignKey = true },
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        });

        result.IsProven.ShouldBeFalse($"{shape} is never proof");
        result.Evidence.ShouldBeNull();
        result.Kind.ShouldBe(shape == "501" ? SessionRunnerAbsenceEvidenceKind.Unsupported : SessionRunnerAbsenceEvidenceKind.Unknown, result.Reason);
        world.Posts.Count.ShouldBe(1, "exactly one POST per read, no retry");
        world.Posts[0].Path.ShouldEndWith("/absence-evidence");
    }

    /// <summary>
    /// V-13. within-5s-valid admits; over-5s-deadline (clock advanced inside the handler) and
    /// expired-validated-result (clock advanced after receipt) refuse; caller-cancellation
    /// propagates OperationCanceledException and is not a failure result. Every case sends
    /// exactly one request with one fresh nonce. No wall-clock sleep.
    /// </summary>
    [Test]
    [Arguments("within-5s-valid")]
    [Arguments("over-5s-deadline")]
    [Arguments("expired-validated-result")]
    [Arguments("caller-cancellation")]
    public async Task C1153_Freshness_and_cancellation_are_bounded(string condition)
    {
        using var world = new ClientWorld();
        using var caller = new CancellationTokenSource();
        var result = default(SessionRunnerAbsenceEvidenceResult);
        switch (condition)
        {
            case "within-5s-valid":
                result = await world.CertifyAsync(_ => ClientWorld.Answer.Pristine with { AdvanceBefore = TimeSpan.FromSeconds(4.9) });
                result.Kind.ShouldBe(SessionRunnerAbsenceEvidenceKind.Proven, result.Reason);
                result.Evidence!.IsFresh(world.Clock.GetUtcNow()).ShouldBeTrue();
                break;
            case "over-5s-deadline":
                result = await world.CertifyAsync(_ => ClientWorld.Answer.Pristine with { AdvanceBefore = TimeSpan.FromSeconds(5.1) });
                result.IsProven.ShouldBeFalse("a reply after the five-second deadline is not proof");
                result.Kind.ShouldBe(SessionRunnerAbsenceEvidenceKind.Unknown);
                result.Reason.ShouldBe("absence_evidence_deadline", "the injected-clock deadline itself ended the read");
                break;
            case "expired-validated-result":
                result = await world.CertifyAsync(_ => ClientWorld.Answer.Pristine);
                result.Kind.ShouldBe(SessionRunnerAbsenceEvidenceKind.Proven, result.Reason);
                result.Evidence!.IsFresh(world.Clock.GetUtcNow()).ShouldBeTrue();
                world.Clock.Advance(TimeSpan.FromSeconds(5.1));
                result.Evidence.IsFresh(world.Clock.GetUtcNow()).ShouldBeFalse("the validated object expires five seconds after receipt");
                break;
            case "caller-cancellation":
                await Should.ThrowAsync<OperationCanceledException>(world.CertifyAsync(_ =>
                {
                    caller.Cancel();
                    return ClientWorld.Answer.Pristine;
                }, caller.Token));
                break;
            default: throw new ArgumentOutOfRangeException(nameof(condition), condition, null);
        }

        world.Posts.Count.ShouldBe(1, "one transport attempt per read");
        RunnerAbsenceEvidence.IsValidNonce(world.Posts[0].Nonce).ShouldBeTrue("each read carries a fresh 32-byte nonce");
    }

    /// <summary>The production HTTP client over a stub runner that signs with the production signer.</summary>
    private sealed class ClientWorld : IDisposable
    {
        public sealed record Answer(
            HttpStatusCode Code, string? RawBody, Action<JsonObject>? Mutate, bool Sign = true, bool ForeignKey = false,
            TimeSpan AdvanceBefore = default)
        {
            public static Answer Pristine => new(HttpStatusCode.OK, null, null);
            public static Answer Shape(Action<JsonObject> mutate) => new(HttpStatusCode.OK, null, mutate);
            public static Answer Raw(string body) => new(HttpStatusCode.OK, body, null);
            public static Answer Status(HttpStatusCode code, string body) => new(code, body, null);
        }

        public sealed record Post(string Path, string Nonce);

        private readonly string _root = Path.Combine(Path.GetTempPath(), "c1153-client-" + Guid.NewGuid().ToString("N"));
        private readonly AbsenceEvidenceKey _key;
        private readonly SessionRunnerHttpClient _client;
        private Func<RunnerAbsenceRequest, Answer> _answer = _ => Answer.Pristine;

        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 8, 8, 0, 0, TimeSpan.Zero));
        public Guid Store { get; } = Guid.NewGuid();
        public Guid SessionId { get; } = Guid.NewGuid();
        public List<Post> Posts { get; } = [];

        public ClientWorld()
        {
            Directory.CreateDirectory(_root);
            var keyPath = Path.Combine(_root, "absence.key");
            var material = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            File.WriteAllText(keyPath, Convert.ToBase64String(material));
            _key = AbsenceEvidenceKey.FromMaterial(material);
            var settings = new SessionRunnerSettings { BaseUrl = "http://runner.test" };
            settings.AbsenceEvidence.KeyPath = keyPath;
            _client = new SessionRunnerHttpClient(new HttpClient(new StubHandler(Respond)), new StubFactory(),
                Options.Create(settings), time: Clock);
        }

        public Task<SessionRunnerAbsenceEvidenceResult> CertifyAsync(Func<RunnerAbsenceRequest, Answer> answer, CancellationToken ct = default)
        {
            _answer = answer;
            return _client.CertifyAbsenceAsync(SessionId, Generation, Store, ct);
        }

        private HttpResponseMessage Respond(HttpRequestMessage request)
        {
            if (request.RequestUri!.AbsolutePath == "/capabilities")
                return Json(new RunnerCapabilitiesDto("InboxConhost", "inbox", "test", false,
                    Features: [RunnerAbsenceEvidence.Feature], RunnerStoreId: Store));
            var body = request.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            using var parsed = JsonDocument.Parse(body);
            var posted = new RunnerAbsenceRequest(parsed.RootElement.GetProperty("version").GetInt32(),
                parsed.RootElement.GetProperty("sessionId").GetGuid(), parsed.RootElement.GetProperty("acceptedStartedAt").GetDateTime(),
                parsed.RootElement.GetProperty("runnerStoreId").GetGuid(), parsed.RootElement.GetProperty("requestNonce").GetString()!);
            Posts.Add(new(request.RequestUri.AbsolutePath, posted.RequestNonce));
            var answer = _answer(posted);
            if (answer.AdvanceBefore > TimeSpan.Zero) Clock.Advance(answer.AdvanceBefore);
            string text;
            if (answer.RawBody is { } raw) text = raw;
            else
            {
                var shape = AbsenceCertificateShape.Pristine(posted.SessionId, posted.AcceptedStartedAt, posted.RunnerStoreId,
                    Guid.NewGuid(), posted.RequestNonce, Clock.GetUtcNow().UtcDateTime);
                answer.Mutate?.Invoke(shape);
                text = shape.ToJsonString();
            }

            var bytes = Encoding.UTF8.GetBytes(text);
            var response = new HttpResponseMessage(answer.Code) { Content = new ByteArrayContent(bytes) };
            if (answer.Sign)
            {
                var signer = answer.ForeignKey ? AbsenceEvidenceKey.FromMaterial(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) : _key;
                response.Headers.Add(AbsenceEvidenceAuthentication.KeyIdHeader, _key.KeyId);
                response.Headers.Add(AbsenceEvidenceAuthentication.MacHeader, AbsenceEvidenceAuthentication.Sign(signer,
                    AbsenceEvidenceAuthentication.ResponseCanonical(AbsenceEvidenceAuthentication.CertifyOperation,
                        posted.RequestNonce, (int)answer.Code, bytes)));
            }

            return response;
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                Encoding.UTF8, "application/json"),
        };

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch (Exception) { }
        }
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = respond(request);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(response);
        }
    }
}
