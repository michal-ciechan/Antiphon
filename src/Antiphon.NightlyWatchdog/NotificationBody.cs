using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Antiphon.NightlyWatchdog;

/// <summary>What a notification says, independent of its attempt number. Persisted with the notification.</summary>
public sealed record NotificationContent(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("outageKind")] string? OutageKind,
    [property: JsonPropertyName("due")] string Due,
    [property: JsonPropertyName("workspace")] string Workspace,
    [property: JsonPropertyName("schedule")] string Schedule,
    [property: JsonPropertyName("job")] string? Job,
    [property: JsonPropertyName("evidence")] string Evidence,
    [property: JsonPropertyName("outageId")] string? OutageId,
    [property: JsonPropertyName("sha")] string? Sha,
    [property: JsonPropertyName("run")] string? Run,
    [property: JsonPropertyName("policy")] string? Policy,
    [property: JsonPropertyName("linkedNid")] string? LinkedNid = null,
    [property: JsonPropertyName("failureReceived")] bool FailureReceived = false)
{
    public string ToJson() => JsonSerializer.Serialize(this);
    public static NotificationContent FromJson(string json) => JsonSerializer.Deserialize<NotificationContent>(json)!;
}

public sealed record ParsedMarker(
    string? Nid, string? Oid, string? Kind, int? Attempt, string? Due, string? H, string? Link, bool? FailureReceived,
    string? Job, string? Run, string? Sha, string? Policy, bool HashMismatch)
{
    public bool Present => Nid != null && Oid != null && Kind != null && Attempt != null && Due != null && H != null;
}

/// <summary>
/// CARD-0545 D-5 body contract. Plain ASCII text; the last line is the machine-parsed marker whose
/// <c>h=</c> is the first 16 lowercase hex of SHA-256 over the lines above it joined by LF. Receipts are
/// matched on SHA-256 of the whole text (D-6 rule 4), never on the marker alone.
/// </summary>
public static class NotificationBody
{
    public const string MarkerPrefix = "#antiphon-nightly ";
    public const int MaxLength = 4096;

    public static string Render(NotificationContent c, string nid, int attempt)
    {
        var head = c.Kind switch
        {
            "failure" => $"Antiphon nightly watchdog: FAILURE {c.OutageKind}",
            "recovery" => $"Antiphon nightly watchdog: RECOVERED {c.OutageKind}",
            _ => "Antiphon nightly watchdog: QUALIFICATION notice",
        };
        var lines = new List<string>
        {
            head,
            $"due {c.Due} Europe/London; workspace {c.Workspace}; schedule {c.Schedule}",
            $"job {c.Job ?? "none"}: {c.Evidence}",
            $"outage {c.OutageId ?? "none"}",
            $"notification {nid}; attempt {attempt}; sha {c.Sha ?? "none"}; run {c.Run ?? "none"}; policy {c.Policy ?? "none"}",
        };
        lines = lines.Select(Clean).ToList();
        var h = Hash16(string.Join('\n', lines));
        var marker = new StringBuilder(MarkerPrefix)
            .Append("nid=").Append(nid)
            .Append(" oid=").Append(c.OutageId ?? "none")
            .Append(" kind=").Append(c.Kind)
            .Append(" attempt=").Append(attempt.ToString(CultureInfo.InvariantCulture))
            .Append(" due=").Append(c.Due);
        if (c.Kind == "recovery")
            marker.Append(" link=").Append(c.LinkedNid ?? "none").Append(" failureReceived=").Append(c.FailureReceived ? "true" : "false");
        marker.Append(" h=").Append(h);
        lines.Add(marker.ToString());
        var text = string.Join('\n', lines);
        return text.Length <= MaxLength ? text : text[..MaxLength];
    }

    public static string RenderFailure(string kind, string due, string workspace, string schedule, string? job, string evidence,
        string outageId, string nid, int attempt, string? sha, string? run, string? policy) =>
        Render(new NotificationContent("failure", kind, due, workspace, schedule, job, evidence, outageId, sha, run, policy), nid, attempt);

    public static string Sha256Hex(string text) => WatchdogOptions.Sha256Hex(text);

    public static string Hash16(string text) => Sha256Hex(text)[..16];

    public static ParsedMarker ParseMarker(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var markerIndex = Array.FindLastIndex(lines, l => l.StartsWith(MarkerPrefix, StringComparison.Ordinal));
        if (markerIndex < 0)
            return new ParsedMarker(null, null, null, null, null, null, null, null, null, null, null, null, false);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var token in lines[markerIndex][MarkerPrefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = token.IndexOf('=');
            if (eq > 0) fields[token[..eq]] = token[(eq + 1)..];
        }
        string? Field(string key) => fields.TryGetValue(key, out var v) ? v : null;
        int? attempt = int.TryParse(Field("attempt"), NumberStyles.None, CultureInfo.InvariantCulture, out var a) ? a : null;
        bool? failureReceived = Field("failureReceived") switch { "true" => true, "false" => false, _ => null };

        string? job = null, run = null, sha = null, policy = null;
        foreach (var line in lines.Take(markerIndex))
        {
            if (line.StartsWith("job ", StringComparison.Ordinal))
            {
                var colon = line.IndexOf(':', 4);
                job = colon > 4 ? line[4..colon] : null;
            }
            else if (line.StartsWith("notification ", StringComparison.Ordinal))
            {
                foreach (var part in line.Split("; ", StringSplitOptions.None))
                {
                    if (part.StartsWith("sha ", StringComparison.Ordinal)) sha = part[4..];
                    else if (part.StartsWith("run ", StringComparison.Ordinal)) run = part[4..];
                    else if (part.StartsWith("policy ", StringComparison.Ordinal)) policy = part[7..];
                }
            }
        }
        var h = Field("h");
        var mismatch = h is null || !string.Equals(Hash16(string.Join('\n', lines.Take(markerIndex))), h, StringComparison.Ordinal);
        return new ParsedMarker(Field("nid"), Field("oid"), Field("kind"), attempt, Field("due"), h, Field("link"), failureReceived,
            job, run, sha, policy, mismatch);
    }

    private static string Clean(string line)
    {
        var chars = line.Select(ch => ch is >= ' ' and <= '~' ? ch : '?').ToArray();
        return new string(chars).TrimEnd();
    }
}
