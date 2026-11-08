using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-1762: the server still reads an old runner's reflection body, and the read goes through
/// the source-generated contract.
/// </summary>
[Category("Unit")]
public class RunnerReadJsonClientTests
{
    private static readonly JsonSerializerOptions ReflectionHttp = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly Guid SessionId = Guid.Parse("4142c507-241f-4234-aa07-c03ebcc7f7d2");
    private static readonly DateTime Started = new(2026, 11, 7, 12, 16, 35, 123, DateTimeKind.Utc);
    private const string Unicode = "café\n日本語 \"quote\" \\slash 😀";

    [Test]
    public async Task Server_reads_an_old_runner_body_through_the_source_generated_contract()
    {
        var buffer = new RunnerBufferDto(SessionId, Unicode, 4);
        var snapshot = new RunnerSnapshotDto(SessionId, Unicode, "", 4, Started);
        RunnerTranscriptEvent entry = new(SessionId, 1, "UserPrompt", null, null, null, "user", Unicode,
            null, null, null, null, null);
        var transcript = new RunnerTranscriptDto(SessionId, [entry], 4);
        var handler = new BodyHandler(new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["/sessions/" + SessionId + "/buffer"] = JsonSerializer.SerializeToUtf8Bytes(buffer, ReflectionHttp),
            ["/sessions/" + SessionId + "/snapshot"] = JsonSerializer.SerializeToUtf8Bytes(snapshot, ReflectionHttp),
            ["/sessions/" + SessionId + "/transcript"] = JsonSerializer.SerializeToUtf8Bytes(transcript, ReflectionHttp),
        });
        var client = new SessionRunnerHttpClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://runner.test/") },
            new StubFactory(),
            Options.Create(new SessionRunnerSettings { BaseUrl = "http://runner.test" }));

        using var watch = RunnerReadJson.Watch();
        var readBuffer = await client.GetBufferAsync(SessionId, CancellationToken.None);
        var readSnapshot = await client.GetSnapshotAsync(SessionId, CancellationToken.None);
        var readTranscript = await client.GetTranscriptAsync(SessionId, CancellationToken.None);

        readBuffer.Buffer.ShouldBe(Unicode);
        readSnapshot.RawOutput.ShouldBe(Unicode);
        readTranscript.Entries.Single().Text.ShouldBe(Unicode);
        watch.Count.ShouldBe(3);
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class BodyHandler(Dictionary<string, byte[]> bodies) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            bodies.TryGetValue(path, out var body).ShouldBeTrue(path);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body!),
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            return Task.FromResult(response);
        }
    }
}
