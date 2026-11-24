using System.Text.Json.Serialization;

namespace Antiphon.Server.Application.Settings;

/// <summary>How people sign in to the review UI and API (<c>Review:Auth</c>).</summary>
public static class ReviewAuthModes
{
    /// <summary>No sign-in: every request acts as <c>Review:DefaultViewerLogin</c>.</summary>
    public const string None = "None";

    /// <summary>Sign in with GitHub; every <c>/api</c> and <c>/hubs</c> request needs a session.</summary>
    public const string GitHub = "GitHub";

    public static bool IsKnown(string? mode) =>
        string.IsNullOrWhiteSpace(mode) || mode.Equals(None, StringComparison.OrdinalIgnoreCase) || mode.Equals(GitHub, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Sign-in with GitHub (<c>Review:Auth</c>). Off by default. With <see cref="Mode"/> <c>GitHub</c>, a
/// person signs in through a GitHub App's (or an OAuth App's) user authorization, and may sign in when
/// their login is in <see cref="AllowedLogins"/> or when they have at least
/// <see cref="RequiredRepoPermission"/> on one of the repos this instance reviews. They decide gates
/// only on those repos (an allowed login, on every repo). Repo access is read once, at sign-in.
/// </summary>
public sealed class ReviewAuthSettings
{
    public const string DefaultClientSecretConfigKey = "Review:Secrets:GitHubOAuthClientSecret";

    /// <summary><c>None</c> (default) or <c>GitHub</c>. Any other value stops the server at startup.</summary>
    public string Mode { get; set; } = ReviewAuthModes.None;

    /// <summary>The GitHub App's Client ID (<c>Iv23…</c>), or an OAuth App's client id.</summary>
    public string? ClientId { get; set; }

    /// <summary>The configuration key that holds the client secret; the value itself never sits in this section.</summary>
    public string ClientSecretConfigKey { get; set; } = DefaultClientSecretConfigKey;

    /// <summary>Resolved from <see cref="ClientSecretConfigKey"/> at startup (Program.cs). Never logged or serialised.</summary>
    [JsonIgnore]
    public string? ClientSecret { get; set; }

    /// <summary>GitHub's web root, where the browser signs in. For Enterprise Server, <c>https://&lt;host&gt;</c>.</summary>
    public string WebBaseUrl { get; set; } = "https://github.com";

    /// <summary>GitHub's API root for <c>GET /user</c>. Absent: <c>https://api.github.com</c>, or <c>{WebBaseUrl}/api/v3</c> for Enterprise Server.</summary>
    public string? ApiBaseUrl { get; set; }

    /// <summary>Logins that may sign in and decide gates on every repo, whatever their repo access.</summary>
    public List<string> AllowedLogins { get; set; } = [];

    /// <summary>
    /// The least access to a reviewed repo that lets a person sign in and decide its gates: <c>read</c>,
    /// <c>write</c> (default) or <c>admin</c>; <c>none</c> lets anyone with a GitHub account sign in and decide anywhere.
    /// </summary>
    public string RequiredRepoPermission { get; set; } = "write";

    /// <summary>How long a session lasts without use (sliding), in hours. Default 168 (a week).</summary>
    public int SessionHours { get; set; } = 168;

    [JsonIgnore]
    public bool IsGitHub => ReviewAuthModes.GitHub.Equals(Mode?.Trim(), StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool ChecksRepoAccess => !RequiredRepoPermission.Trim().Equals("none", StringComparison.OrdinalIgnoreCase);

    public bool IsAllowedLogin(string login) =>
        AllowedLogins.Any(l => string.Equals(l.Trim().TrimStart('@'), login, StringComparison.OrdinalIgnoreCase));

    public string ResolvedApiBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(ApiBaseUrl))
            return ApiBaseUrl.TrimEnd('/');
        var web = WebBaseUrl.TrimEnd('/');
        return web.Equals("https://github.com", StringComparison.OrdinalIgnoreCase) ? "https://api.github.com" : web + "/api/v3";
    }

    /// <summary>A GitHub permission's rank: none 0, read (and triage) 1, write (and maintain) 2, admin 3.</summary>
    public static int Rank(string? permission) => permission?.Trim().ToLowerInvariant() switch
    {
        "admin" => 3,
        "write" or "maintain" or "push" => 2,
        "read" or "triage" or "pull" => 1,
        _ => 0,
    };

    /// <summary>Why the settings cannot work, or null.</summary>
    public string? Problem()
    {
        if (!ReviewAuthModes.IsKnown(Mode))
            return $"Review:Auth:Mode '{Mode}' is not None or GitHub.";
        if (!IsGitHub)
            return null;
        if (string.IsNullOrWhiteSpace(ClientId))
            return "Review:Auth:Mode is GitHub but Review:Auth:ClientId is not set.";
        if (string.IsNullOrWhiteSpace(ClientSecret))
            return $"Review:Auth:Mode is GitHub but the client secret ({ClientSecretConfigKey}) is not set.";
        if (ChecksRepoAccess && Rank(RequiredRepoPermission) == 0)
            return $"Review:Auth:RequiredRepoPermission '{RequiredRepoPermission}' is not read, write, admin or none.";
        if (SessionHours <= 0)
            return "Review:Auth:SessionHours must be positive.";
        return null;
    }
}
