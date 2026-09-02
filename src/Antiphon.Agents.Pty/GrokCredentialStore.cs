using System.Text.Json;

namespace Antiphon.Agents.Pty;

/// <summary>
/// File-only inspection of Grok 1.0.13's OAuth credential store (CARD-0324).
/// The screen detector is the ground truth; this probe is the fast path that
/// saves a worktree and a 60 s ready wait when <c>auth.json</c> is already gone.
/// </summary>
public static class GrokCredentialStore
{
    public enum Finding
    {
        Present = 0,
        Absent = 1,
        Empty = 2,
        ApiKeyAuth = 3,
    }

    /// <summary>
    /// <c>GROK_HOME</c> from the launch env, else this process, else <c>~/.grok</c>.
    /// Same fallback chain as <c>GrokTranscriptTailer.ResolveGrokHome</c>.
    /// </summary>
    public static string ResolveGrokHome(IReadOnlyDictionary<string, string>? launchEnv = null)
    {
        string? grokHome = null;
        launchEnv?.TryGetValue("GROK_HOME", out grokHome);
        if (string.IsNullOrWhiteSpace(grokHome))
            grokHome = Environment.GetEnvironmentVariable("GROK_HOME");
        if (string.IsNullOrWhiteSpace(grokHome))
            grokHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grok");
        return grokHome;
    }

    /// <summary>
    /// True when this launch authenticates with an API key / provider command rather than
    /// the OAuth store (the standing <c>gkp</c> profile's case).
    /// </summary>
    public static bool UsesApiKeyAuth(IReadOnlyDictionary<string, string>? launchEnv)
    {
        if (launchEnv is not null)
        {
            return HasNonEmpty(launchEnv, "XAI_API_KEY")
                || HasNonEmpty(launchEnv, "GROK_CODE_XAI_API_KEY")
                || HasNonEmpty(launchEnv, "GROK_AUTH_PROVIDER_COMMAND");
        }

        return HasProcessApiKeyAuth();
    }

    /// <summary>
    /// Inspect the store for a launch. Unreadable files are treated as
    /// <see cref="Finding.Present"/> — a probe must never block a launch that would have worked.
    /// </summary>
    public static Finding Inspect(string grokHome, IReadOnlyDictionary<string, string>? launchEnv = null)
    {
        if (UsesApiKeyAuth(launchEnv))
            return Finding.ApiKeyAuth;

        var path = ResolveAuthPath(grokHome, launchEnv);
        try
        {
            if (!File.Exists(path))
            {
                // A directory occupying the auth.json path is unreadable, not Absent
                // (File.Exists returns false for directories on Windows).
                return Directory.Exists(path) ? Finding.Present : Finding.Absent;
            }

            var json = File.ReadAllText(path);
            return HasUsableScope(json) ? Finding.Present : Finding.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Finding.Present;
        }
    }

    public static bool IsLaunchBlocking(Finding finding) =>
        finding is Finding.Absent or Finding.Empty;

    private static string ResolveAuthPath(string grokHome, IReadOnlyDictionary<string, string>? launchEnv)
    {
        string? overridePath = null;
        launchEnv?.TryGetValue("GROK_AUTH_PATH", out overridePath);
        if (string.IsNullOrWhiteSpace(overridePath))
            overridePath = Environment.GetEnvironmentVariable("GROK_AUTH_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;
        return Path.Combine(grokHome, "auth.json");
    }

    private static bool HasProcessApiKeyAuth() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("XAI_API_KEY"))
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GROK_CODE_XAI_API_KEY"))
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GROK_AUTH_PROVIDER_COMMAND"));

    private static bool HasNonEmpty(IReadOnlyDictionary<string, string> env, string name) =>
        env.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value);

    private static bool HasUsableScope(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return false;

        foreach (var scope in doc.RootElement.EnumerateObject())
        {
            if (scope.Value.ValueKind != JsonValueKind.Object)
                continue;
            if (!TryGetNonEmptyString(scope.Value, "key", out _))
                continue;

            var expired = IsExpired(scope.Value);
            if (!expired)
                return true;
            if (TryGetNonEmptyString(scope.Value, "refresh_token", out _))
                return true;
        }

        return false;
    }

    private static bool IsExpired(JsonElement scope)
    {
        if (!scope.TryGetProperty("expires_at", out var expires))
            return false;

        DateTimeOffset expiry;
        switch (expires.ValueKind)
        {
            case JsonValueKind.Number when expires.TryGetInt64(out var unix):
                expiry = DateTimeOffset.FromUnixTimeSeconds(unix);
                break;
            case JsonValueKind.String when DateTimeOffset.TryParse(expires.GetString(), out var parsed):
                expiry = parsed;
                break;
            default:
                return false;
        }

        return expiry <= DateTimeOffset.UtcNow;
    }

    private static bool TryGetNonEmptyString(JsonElement obj, string name, out string value)
    {
        value = "";
        if (!obj.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.String)
            return false;
        var text = prop.GetString();
        if (string.IsNullOrWhiteSpace(text))
            return false;
        value = text;
        return true;
    }
}
