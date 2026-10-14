using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Tests.TestHelpers;

namespace Antiphon.Tests.Scripts;

/// <summary>Loopback script fixture with ordered requests, response overrides and read-back state.</summary>
internal class ScriptedApi : IDisposable
{
    internal sealed record Request(string Method, string Path, JsonElement? Body);
    private sealed record Response(string Method, string Prefix, int Status, string Body);
    private readonly HttpListener _listener = new();
    private readonly Task _pump;
    private readonly ConcurrentQueue<Request> _requests = new();
    private readonly List<Response> _responses = [];

    public ScriptedApi()
    {
        BaseUrl = EphemeralHttpListener.BindLoopback(_listener);
        _pump = Task.Run(PumpAsync);
    }

    public string BaseUrl { get; }
    public IReadOnlyList<Request> Requests => _requests.ToArray();
    public int RequestCount => _requests.Count;
    public string? LastMethod => Requests.LastOrDefault()?.Method;
    public string? LastPath => Requests.LastOrDefault()?.Path;
    public JsonDocument? LastBody { get; private set; }
    public Dictionary<string, JsonObject> Cells { get; } = [];
    public List<JsonObject> Pins { get; } = [];

    public void Respond(string method, string pathPrefix, int status, string body) =>
        _responses.Add(new Response(method, pathPrefix, status, body));

    private async Task PumpAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { return; }

            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var raw = await reader.ReadToEndAsync();
            JsonElement? body = null;
            if (!string.IsNullOrWhiteSpace(raw))
            {
                LastBody?.Dispose();
                LastBody = JsonDocument.Parse(raw);
                body = LastBody.RootElement.Clone();
            }
            var request = new Request(context.Request.HttpMethod, context.Request.Url!.PathAndQuery, body);
            _requests.Enqueue(request);
            var response = _responses.LastOrDefault(r => r.Method == request.Method
                && request.Path.StartsWith(r.Prefix, StringComparison.Ordinal));
            var (status, payload) = response is null
                ? Handle(request, context.Request.QueryString["role"])
                : (response.Status, response.Body);
            context.Response.StatusCode = status;
            if (status != 204)
            {
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(payload));
            }
            context.Response.Close();
        }
    }

    private (int Status, string Body) Handle(Request request, string? role)
    {
        const string cellsPrefix = "/api/complexity-chains/";
        if (request.Path.StartsWith(cellsPrefix, StringComparison.Ordinal))
        {
            var key = request.Path[cellsPrefix.Length..];
            if (request.Method == "PUT")
            {
                Cells[key] = JsonNode.Parse(request.Body!.Value.GetRawText())!.AsObject();
                return (200, CellView(key).ToJsonString());
            }
            if (request.Method == "DELETE")
            {
                Cells.Remove(key);
                return (204, "");
            }
        }
        if (request.Method == "GET" && request.Path.StartsWith("/api/complexity-chains", StringComparison.Ordinal))
        {
            var keys = role is null
                ? new[] { "any/Hard", "any/Medium", "any/Easy" }.Concat(Cells.Keys).Distinct()
                : new[] { $"{role}/Hard", $"{role}/Medium", $"{role}/Easy" };
            var chains = new JsonArray(keys.Select(key => (JsonNode)CellView(key)).ToArray());
            return (200, new JsonObject { ["chains"] = chains }.ToJsonString());
        }
        if (request.Method == "GET" && request.Path.StartsWith("/api/routing-pins", StringComparison.Ordinal))
        {
            var pins = Pins.Where(pin => role is null || pin["role"]?.GetValue<string>() == role);
            return (200, new JsonObject { ["pins"] = new JsonArray(pins.Select(pin => pin.DeepClone()).ToArray()) }.ToJsonString());
        }
        if (request.Method == "DELETE" && request.Path.StartsWith("/api/routing-pins/", StringComparison.Ordinal))
        {
            var id = request.Path["/api/routing-pins/".Length..];
            Pins.RemoveAll(pin => pin["id"]?.GetValue<string>() == id);
            return (204, "");
        }
        return (404, "{\"detail\":\"Unexpected script request\"}");
    }

    private JsonObject CellView(string key)
    {
        var segments = key.Split('/');
        var present = Cells.TryGetValue(key, out var stored);
        var view = stored?.DeepClone().AsObject() ?? new JsonObject { ["candidates"] = new JsonArray() };
        view["role"] = segments[0] == "any" ? null : segments[0];
        view["complexity"] = segments[1];
        view["resolvedFrom"] = present ? "role" : "none";
        view["source"] = present ? "pin" : "config";
        return view;
    }

    public void Dispose()
    {
        _listener.Close();
        _pump.GetAwaiter().GetResult();
        LastBody?.Dispose();
    }
}
