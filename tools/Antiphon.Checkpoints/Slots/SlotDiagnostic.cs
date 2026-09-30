using System.Text.Json;

namespace Antiphon.Checkpoints;

public sealed record SlotDiagnostic(
    string Operation,
    int? Status,
    string Reason,
    string? Detail,
    string? Exception,
    int ElapsedSeconds)
{
    public static SlotDiagnostic Answer(string operation, int status, string reason, string body, int elapsed) =>
        new(operation, status, reason, Excerpt(body), null, elapsed);

    public static SlotDiagnostic Failure(string operation, string reason, Exception error, int elapsed) =>
        new(operation, null, reason, null, Excerpt(error.GetType().Name + ": " + error.Message), elapsed);

    public string Line(string? label = null) =>
        "BUILD SLOT " + (label is null ? "" : "label=" + Excerpt(label) + " ") +
        $"operation={Operation} status={(Status?.ToString() ?? "none")} reason={Reason} elapsed={ElapsedSeconds}s" +
        (Detail is null ? "" : " detail=" + Detail) +
        (Exception is null ? "" : " exception=" + Exception);

    public static string ReasonOf(string body, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
                return type.GetString() is { Length: > 0 } value ? value : fallback;
        }
        catch (JsonException) { }
        return fallback;
    }

    public static string Excerpt(string value)
    {
        var token = Environment.GetEnvironmentVariable("ANTIPHON_TASK_TOKEN");
        if (!string.IsNullOrEmpty(token))
            value = value.Replace(token, "[redacted]", StringComparison.Ordinal);
        var text = JsonSerializer.Serialize(value);
        if (text.Length <= 2048)
            return text;
        return text[..2033] + "...[truncated]";
    }
}
