using System.Text.Json;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The observer document at <c>logs/apphost-watchdog-state.json</c> (CARD-0245 S1).
/// Written by <c>scripts/apphost-watchdog-state-observer.ps1</c>; the server never writes it.
/// </summary>
public sealed record AppHostWatchdogStateDocument(
    DateTimeOffset ObservedAtUtc,
    string TaskName,
    string State,
    bool Healthy,
    bool Maintenance,
    DateTimeOffset? DisabledSinceUtc,
    Guid? EpisodeId,
    string? Detail)
{
    public const string StateEnabled = "Enabled";
    public const string StateDisabled = "Disabled";
    public const string StateMissing = "Missing";
    public const string StateUnknown = "Unknown";

    public bool IsUnhealthy =>
        State is StateDisabled or StateMissing or StateUnknown;

    public static bool TryParse(string json, out AppHostWatchdogStateDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var parsed = JsonDocument.Parse(json);
            var root = parsed.RootElement;
            var state = ReadString(root, "state");
            if (string.IsNullOrWhiteSpace(state))
                return false;

            document = new AppHostWatchdogStateDocument(
                ObservedAtUtc: ReadTime(root, "observedAtUtc") ?? DateTimeOffset.UnixEpoch,
                TaskName: ReadString(root, "taskName") ?? "Antiphon AppHost Watchdog",
                State: state,
                Healthy: ReadBool(root, "healthy") ?? state == StateEnabled,
                Maintenance: ReadBool(root, "maintenance") ?? false,
                DisabledSinceUtc: ReadTime(root, "disabledSinceUtc"),
                EpisodeId: ReadGuid(root, "episodeId"),
                Detail: ReadString(root, "detail"));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string ResolveDocumentPath(string? configured, string contentRoot)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (Path.IsPathRooted(configured))
                return configured;
            return Path.GetFullPath(Path.Combine(contentRoot, configured));
        }

        var dir = new DirectoryInfo(contentRoot);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                return Path.Combine(dir.FullName, "logs", "apphost-watchdog-state.json");
            dir = dir.Parent;
        }

        return Path.GetFullPath(Path.Combine(contentRoot, "logs", "apphost-watchdog-state.json"));
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? ReadBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            ? value.GetBoolean()
            : null;

    private static DateTimeOffset? ReadTime(JsonElement root, string name)
    {
        var text = ReadString(root, name);
        return DateTimeOffset.TryParse(text, out var parsed) ? parsed.ToUniversalTime() : null;
    }

    private static Guid? ReadGuid(JsonElement root, string name)
    {
        var text = ReadString(root, name);
        return Guid.TryParse(text, out var parsed) ? parsed : null;
    }
}
