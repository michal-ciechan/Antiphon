using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0502: pipe-backed SSE handler for <c>SessionRunnerHttpClient.StreamEventsAsync</c>.
/// GET /events reads from the pipe; every other route answers from <see cref="JsonRoutes"/>.
/// </summary>
public sealed class SseStreamHandler : HttpMessageHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Pipe _events = new(new PipeOptions(pauseWriterThreshold: 0, resumeWriterThreshold: 0));
    private readonly List<HttpRequestMessage> _requests = [];
    private readonly object _gate = new();

    public IReadOnlyList<HttpRequestMessage> Requests
    {
        get { lock (_gate) return _requests.ToList(); }
    }

    /// <summary>Absolute-path answers (for example <c>/capabilities</c>, <c>/sessions</c>).</summary>
    public Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> JsonRoutes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<RunnerSessionDto> ListedSessions { get; set; } = [];
    public RunnerSessionDto? GetSession { get; set; }

    public SseStreamHandler()
    {
        JsonRoutes["/capabilities"] = _ => Json(new RunnerCapabilitiesDto(
            "InboxConhost", "inbox", "test", false,
            Features: [RunnerCapabilityFeatures.SessionGenerationV1]));
        JsonRoutes["/sessions"] = _ => Json(ListedSessions);
    }

    public async Task WriteSseAsync(string eventName, string json, CancellationToken ct = default)
    {
        var frame = Encoding.UTF8.GetBytes($"event: {eventName}\ndata: {json}\n\n");
        await _events.Writer.WriteAsync(frame, ct);
        await _events.Writer.FlushAsync(ct);
    }

    public void Complete() => _events.Writer.Complete();

    public IHttpClientFactory Factory() => new HandlerFactory(this);

    public SessionRunnerHttpClient Client(string baseUrl = "http://runner.test") =>
        new(
            new HttpClient(this) { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") },
            Factory(),
            Microsoft.Extensions.Options.Options.Create(
                new Antiphon.Server.Application.Settings.SessionRunnerSettings
                {
                    BaseUrl = baseUrl,
                    Enabled = true,
                    EventStreamIdleTimeoutSeconds = 120,
                    EventReconnectDelayMs = 50,
                }));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_gate)
            _requests.Add(request);

        var path = request.RequestUri?.AbsolutePath ?? "";
        if (request.Method == HttpMethod.Get && path.Equals("/events", StringComparison.OrdinalIgnoreCase))
        {
            var content = new StreamContent(_events.Reader.AsStream());
            content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }

        if (JsonRoutes.TryGetValue(path, out var route))
            return Task.FromResult(route(request));

        if (request.Method == HttpMethod.Get && path.StartsWith("/sessions/", StringComparison.OrdinalIgnoreCase)
            && path.EndsWith("/transcript", StringComparison.OrdinalIgnoreCase))
        {
            var id = Guid.Parse(path.Split('/')[2]);
            return Task.FromResult(Json(new RunnerTranscriptDto(id, [], 0)));
        }

        if (request.Method == HttpMethod.Get && path.StartsWith("/sessions/", StringComparison.OrdinalIgnoreCase)
            && path.Split('/').Length == 3)
        {
            var id = Guid.Parse(path.Split('/')[2]);
            var dto = GetSession ?? ListedSessions.FirstOrDefault(s => s.SessionId == id);
            if (dto is null)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(Json(dto));
        }

        if (request.Method == HttpMethod.Post && path.Equals("/sessions", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(EchoLaunch(request));
        }

        if (request.Method == HttpMethod.Post && path.EndsWith("/kill-generation", StringComparison.OrdinalIgnoreCase))
        {
            var id = Guid.Parse(path.Split('/')[2]);
            return Task.FromResult(Json(new RunnerKillGenerationResult(id, false, KillGenerationOutcomes.Mismatch, null)));
        }

        if (request.Method == HttpMethod.Post && path.EndsWith("/kill", StringComparison.OrdinalIgnoreCase))
        {
            var id = Guid.Parse(path.Split('/')[2]);
            return Task.FromResult(Json(new RunnerSessionDto(id, null, DateTime.UtcNow, "Exited", 1, "KilledByRequest", 0)));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static HttpResponseMessage EchoLaunch(HttpRequestMessage request)
    {
        var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "{}";
        using var doc = JsonDocument.Parse(body);
        var sessionId = doc.RootElement.GetProperty("sessionId").GetGuid();
        DateTime? generation = null;
        if (doc.RootElement.TryGetProperty("acceptedStartedAt", out var field)
            && field.ValueKind == JsonValueKind.String
            && DateTime.TryParse(field.GetString(), out var parsed))
        {
            generation = SessionGeneration.Normalize(parsed);
        }

        return Json(new RunnerSessionDto(
            sessionId, 1, DateTime.UtcNow, "Running", null, "", 0, AcceptedStartedAt: generation));
    }

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json"),
    };

    private sealed class HandlerFactory(SseStreamHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://runner.test/") };
    }
}
