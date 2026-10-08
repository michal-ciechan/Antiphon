using System.Globalization;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner;

/// <summary>CARD-1153 D-3: the direct-HTTP key, loaded once from configuration custody.</summary>
public sealed class AbsenceEvidenceKeyProvider
{
    public AbsenceEvidenceKey? Key { get; }

    public AbsenceEvidenceKeyProvider(AbsenceEvidenceKey? key) => Key = key;

    public AbsenceEvidenceKeyProvider(IOptions<SessionRunnerSettings> settings, ILogger<AbsenceEvidenceKeyProvider> logger)
    {
        Key = AbsenceEvidenceKey.TryLoad(settings.Value.AbsenceEvidence.KeyPath, out var problem);
        if (Key is null)
            logger.LogInformation("Direct-HTTP absence evidence disabled: {Problem}", problem);
        else
            logger.LogInformation("Direct-HTTP absence evidence key {KeyId} loaded", Key.KeyId);
    }
}

/// <summary>
/// CARD-1153: <c>POST /sessions/{id}/absence-evidence/prepare</c> and
/// <c>POST /sessions/{id}/absence-evidence</c>. Thin adapters over the runtime's evidence service.
/// Requests and responses are HMAC-authenticated; the route id, body id and signed id must be one
/// value (A-1); every answer is <c>Cache-Control: no-store</c>. Neither route resolves a session
/// through <c>GetSession</c>: an unknown id is answered by the evidence store, never by a 404 throw.
/// </summary>
public static class AbsenceEvidenceRoutes
{
    public const string PrepareRoute = "/sessions/{id:guid}/absence-evidence/prepare";
    public const string CertifyRoute = "/sessions/{id:guid}/absence-evidence";
    private const int MaxBodyBytes = 16 * 1024;

    private static readonly string[] RequestMembers =
        ["version", "sessionId", "acceptedStartedAt", "runnerStoreId", "requestNonce"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapAbsenceEvidenceRoutes(this IEndpointRouteBuilder app)
    {
        app.MapPost(PrepareRoute, (Guid id, HttpContext http, SessionRunnerRuntime runtime, AbsenceEvidenceKeyProvider keys,
            CancellationToken ct) => HandleAsync(AbsenceEvidenceAuthentication.PrepareOperation, id, http, keys,
                async request => Serialize(await runtime.PrepareAbsenceEvidenceAsync(request, ct)), ct));
        app.MapPost(CertifyRoute, (Guid id, HttpContext http, SessionRunnerRuntime runtime, AbsenceEvidenceKeyProvider keys,
            CancellationToken ct) => HandleAsync(AbsenceEvidenceAuthentication.CertifyOperation, id, http, keys,
                async request => Serialize(await runtime.CertifyAbsenceAsync(request, ct)), ct));
    }

    private static async Task HandleAsync(
        string operation, Guid routeId, HttpContext http, AbsenceEvidenceKeyProvider keys,
        Func<RunnerAbsenceRequest, Task<(int Status, byte[] Body)>> decide, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        var body = await ReadBodyAsync(http.Request, ct);
        if (keys.Key is not { } key)
        {
            // Missing key: this transport cannot certify. No state is read or changed.
            await WriteAsync(http, null, operation, "", Problem(RunnerAbsenceRefusalCodes.Unauthenticated, 401,
                "direct-HTTP absence evidence is not configured"));
            return;
        }

        var parsed = body is null ? null : ParseRequest(body);
        var nonce = parsed?.RequestNonce ?? "";
        if (!string.Equals(http.Request.Headers[AbsenceEvidenceAuthentication.KeyIdHeader].ToString(), key.KeyId, StringComparison.Ordinal))
        {
            await WriteAsync(http, key, operation, nonce, Problem(RunnerAbsenceRefusalCodes.Unauthenticated, 401, "unknown key id"));
            return;
        }

        if (body is null || parsed is null)
        {
            await WriteAsync(http, key, operation, nonce, Problem(RunnerAbsenceRefusalCodes.InvalidRequest, 400, "malformed request body"));
            return;
        }

        var canonical = AbsenceEvidenceAuthentication.RequestCanonical(
            operation, routeId, parsed.AcceptedStartedAt, parsed.RunnerStoreId, parsed.RequestNonce, body);
        if (!AbsenceEvidenceAuthentication.Verify(key, canonical, http.Request.Headers[AbsenceEvidenceAuthentication.MacHeader].ToString()))
        {
            await WriteAsync(http, key, operation, nonce, Problem(RunnerAbsenceRefusalCodes.Unauthenticated, 401, "request MAC mismatch"));
            return;
        }

        if (parsed.SessionId != routeId)
        {
            await WriteAsync(http, key, operation, nonce, Problem(RunnerAbsenceRefusalCodes.InvalidRequest, 400, "route and body session ids differ"));
            return;
        }

        await WriteAsync(http, key, operation, nonce, await decide(parsed));
    }

    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>Strict version 1 request: the exact member set, exact types, no extras.</summary>
    internal static RunnerAbsenceRequest? ParseRequest(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            var names = root.EnumerateObject().Select(p => p.Name).ToList();
            if (names.Count != RequestMembers.Length || names.Distinct(StringComparer.Ordinal).Count() != names.Count
                || !names.All(n => RequestMembers.Contains(n, StringComparer.Ordinal)))
                return null;
            var version = root.GetProperty("version");
            if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v))
                return null;
            if (!TryGuid(root.GetProperty("sessionId"), out var sessionId)
                || !TryGuid(root.GetProperty("runnerStoreId"), out var store))
                return null;
            var stamp = root.GetProperty("acceptedStartedAt");
            if (stamp.ValueKind != JsonValueKind.String
                || !DateTime.TryParse(stamp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var generation)
                || generation.Kind != DateTimeKind.Utc)
                return null;
            var nonce = root.GetProperty("requestNonce");
            if (nonce.ValueKind != JsonValueKind.String)
                return null;
            return new RunnerAbsenceRequest(v, sessionId, generation, store, nonce.GetString()!);
        }
        catch (JsonException) { return null; }
    }

    private static bool TryGuid(JsonElement element, out Guid value)
    {
        value = Guid.Empty;
        return element.ValueKind == JsonValueKind.String && Guid.TryParseExact(element.GetString(), "D", out value);
    }

    private static (int Status, byte[] Body) Serialize<T>(RunnerAbsenceOutcome<T> outcome) where T : class =>
        outcome.Value is { } value
            ? (200, JsonSerializer.SerializeToUtf8Bytes(value, Json))
            : Problem(outcome.Refusal!.Code, outcome.Refusal.Status, outcome.Refusal.Reason);

    internal static (int Status, byte[] Body) Problem(string code, int status, string detail) =>
        (status, JsonSerializer.SerializeToUtf8Bytes(new { type = code, title = code, status, detail }, Json));

    private static async Task WriteAsync(HttpContext http, AbsenceEvidenceKey? key, string operation, string nonce,
        (int Status, byte[] Body) answer)
    {
        http.Response.StatusCode = answer.Status;
        http.Response.ContentType = answer.Status == 200 ? "application/json" : "application/problem+json";
        if (key is not null)
        {
            http.Response.Headers[AbsenceEvidenceAuthentication.KeyIdHeader] = key.KeyId;
            http.Response.Headers[AbsenceEvidenceAuthentication.MacHeader] = AbsenceEvidenceAuthentication.Sign(key,
                AbsenceEvidenceAuthentication.ResponseCanonical(operation, nonce, answer.Status, answer.Body));
        }

        await http.Response.Body.WriteAsync(answer.Body);
    }
}
