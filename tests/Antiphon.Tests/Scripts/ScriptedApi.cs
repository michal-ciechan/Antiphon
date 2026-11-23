using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// An in-process stand-in for the complexity chain and routing pin API (unit 26 of the 2026-11-22 plan: the complexity-chain
/// and migrate-stage-pins-to-cells commands, which replaced the scripts this fixture served over a loopback listener). It
/// records every request in order, answers a matching override (the last one registered whose method and path prefix match)
/// or its read-back state (cells with their CARD-1275 tokens, pins), and never leaves the process.
/// </summary>
internal class ScriptedApi
{
    /// <summary>The fake API base the commands are given; the transport answers instead.</summary>
    public const string BaseUrl = "http://127.0.0.1:17202/";

    internal sealed record Request(string Method, string Path, JsonElement? Body, IReadOnlyDictionary<string, string> Headers);
    private sealed record Response(string Method, string Prefix, int Status, string Body);
    private readonly List<Request> _requests = [];
    private readonly List<Response> _responses = [];

    public IReadOnlyList<Request> Requests
    {
        get { lock (_requests) return _requests.ToArray(); }
    }

    public int RequestCount => Requests.Count;
    public string? LastMethod => Requests.LastOrDefault()?.Method;
    public string? LastPath => Requests.LastOrDefault()?.Path;
    public JsonDocument? LastBody { get; private set; }
    public Dictionary<string, JsonObject> Cells { get; } = [];
    /// <summary>CARD-1275: the row token each PUT issues; GET shows it, DELETE removes it.</summary>
    public Dictionary<string, string> Tokens { get; } = [];
    public List<JsonObject> Pins { get; } = [];

    public void Respond(string method, string pathPrefix, int status, string body) =>
        _responses.Add(new Response(method, pathPrefix, status, body));

    /// <summary>The commands' HTTP transport: one request in, its answer out.</summary>
    public NightlyReportHttp Transport => (method, uri, headers, body) =>
    {
        JsonElement? parsed = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            LastBody = JsonDocument.Parse(body);
            parsed = LastBody.RootElement.Clone();
        }
        var request = new Request(method, new Uri(uri).PathAndQuery, parsed, new Dictionary<string, string>(headers ?? new Dictionary<string, string>()));
        lock (_requests) _requests.Add(request);
        var response = _responses.LastOrDefault(r => r.Method == request.Method && request.Path.StartsWith(r.Prefix, StringComparison.Ordinal));
        var (status, payload) = response is null ? Handle(request, Role(request.Path)) : (response.Status, response.Body);
        return new NightlyReportHttpResponse(status, status == 204 ? "" : payload);
    };

    private static string? Role(string path)
    {
        var query = path.IndexOf('?');
        if (query < 0) return null;
        foreach (var pair in path[(query + 1)..].Split('&'))
            if (pair.StartsWith("role=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(pair["role=".Length..]);
        return null;
    }

    private (int Status, string Body) Handle(Request request, string? role)
    {
        const string cellsPrefix = "/api/complexity-chains/";
        if (request.Path.StartsWith(cellsPrefix, StringComparison.Ordinal))
        {
            var key = request.Path[cellsPrefix.Length..].Split('?')[0];
            if (request.Method == "PUT")
            {
                Cells[key] = JsonNode.Parse(request.Body!.Value.GetRawText())!.AsObject();
                Tokens[key] = Guid.NewGuid().ToString();
                return (200, CellView(key).ToJsonString());
            }
            if (request.Method == "DELETE")
            {
                Cells.Remove(key);
                Tokens.Remove(key);
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
        view["concurrencyToken"] = Tokens.TryGetValue(key, out var token) ? token : null;
        return view;
    }
}
