using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Checkpoints;

public sealed record SlotDiagnostic(
    string Operation,
    int? Status,
    string Reason,
    string? Detail,
    string? Exception,
    int ElapsedSeconds)
{
    public static SlotDiagnostic Answer(string operation, int status, string reason, string body, int elapsed,
        string? sensitiveToken = null) =>
        new(operation, status, reason, Excerpt(body, sensitiveToken), null, elapsed);

    public static SlotDiagnostic Failure(string operation, string reason, Exception error, int elapsed,
        string? sensitiveToken = null) =>
        new(operation, null, reason, null, Excerpt(error.GetType().Name + ": " + error.Message, sensitiveToken), elapsed);

    public string Line(string? label = null, string? sensitiveToken = null) =>
        "BUILD SLOT " + (label is null ? "" : "label=" + Excerpt(label, sensitiveToken) + " ") +
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
                return type.GetString() is { Length: > 0 } value
                    && Regex.IsMatch(value, "^[a-z][a-z0-9_]{0,79}$", RegexOptions.CultureInvariant)
                    ? value : fallback;
        }
        catch (JsonException) { }
        return fallback;
    }

    public static string Excerpt(string value, string? sensitiveToken = null)
    {
        var token = Environment.GetEnvironmentVariable("ANTIPHON_TASK_TOKEN");
        if (!string.IsNullOrEmpty(token))
            value = value.Replace(token, "[redacted]", StringComparison.Ordinal);
        if (!string.IsNullOrEmpty(sensitiveToken))
            value = value.Replace(sensitiveToken, "[redacted]", StringComparison.Ordinal);
        value = Regex.Replace(value, @"https?://[^\s""'<>]+", match =>
        {
            if (!Uri.TryCreate(match.Value, UriKind.Absolute, out var uri))
                return "[url]";
            var safe = new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" };
            return safe.Uri.GetLeftPart(UriPartial.Path);
        }, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var text = JsonSerializer.Serialize(value);
        if (text.Length <= 2048)
            return text;
        return text[..2033] + "...[truncated]";
    }
}
