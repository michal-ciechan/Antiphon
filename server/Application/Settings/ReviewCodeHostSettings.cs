using System.Text.Json.Serialization;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Settings;

/// <summary>
/// Review engine code hosts (design 8), section <c>Review:CodeHosts</c>. <see cref="Hosts"/> holds the
/// hosts by id; <see cref="Repos"/> maps a repo to a host and, for local git, a path. A GitHub repo
/// with no row uses the host <c>github</c>; a local repo needs a row with its path. A host names its
/// token by configuration key only (<see cref="ReviewCodeHost.TokenConfigKey"/>); Program.cs resolves
/// it into <see cref="ReviewCodeHost.Token"/>, which is never serialised or logged.
/// </summary>
public sealed class ReviewCodeHostSettings
{
    public const string SectionName = "Review:CodeHosts";
    public const string DefaultGitHubHostId = "github";
    public const string DefaultLocalHostId = "local";
    public const string DefaultGitHubTokenConfigKey = "Review:Secrets:GitHubToken";

    public Dictionary<string, ReviewCodeHost> Hosts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<ReviewCodeHostRepo> Repos { get; set; } = [];

    /// <summary>Adds <c>github</c> (api.github.com, token at <see cref="DefaultGitHubTokenConfigKey"/>) and <c>local</c> when absent.</summary>
    public void ApplyDefaults()
    {
        if (!Hosts.ContainsKey(DefaultGitHubHostId))
            Hosts[DefaultGitHubHostId] = new ReviewCodeHost { Kind = ReviewHost.GitHub, TokenConfigKey = DefaultGitHubTokenConfigKey };
        if (!Hosts.ContainsKey(DefaultLocalHostId))
            Hosts[DefaultLocalHostId] = new ReviewCodeHost { Kind = ReviewHost.Local };
    }

    public ReviewCodeHostRepo? FindRepo(string repo) =>
        Repos.FirstOrDefault(r => string.Equals(r.Repo.Trim(), repo, StringComparison.OrdinalIgnoreCase));
}

public sealed class ReviewCodeHost
{
    public const string DefaultGitHubApiRoot = "https://api.github.com";

    public ReviewHost Kind { get; set; } = ReviewHost.GitHub;

    /// <summary>
    /// The REST API root: absent means <c>https://api.github.com</c>; GitHub Enterprise Server is
    /// <c>https://ghe.example.com/api/v3</c>. A company proxy goes here too.
    /// </summary>
    public string? BaseUrl { get; set; }

    internal Uri GitHubApiRoot
    {
        get
        {
            var configured = string.IsNullOrWhiteSpace(BaseUrl) ? DefaultGitHubApiRoot : BaseUrl.Trim();
            if (!Uri.TryCreate(configured.TrimEnd('/') + "/", UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                throw new Antiphon.Server.Application.Interfaces.CodeHostException(
                    Antiphon.Server.Application.Interfaces.CodeHostFailureKind.NotConfigured,
                    "The GitHub REST API root must be an HTTP(S) URL without credentials, a query or a fragment.");
            return uri;
        }
    }

    internal string GitHubAuthority => GitHubApiRoot.GetLeftPart(UriPartial.Path).TrimEnd('/');

    /// <summary>
    /// The GraphQL endpoint (thread resolution). Absent, it follows <see cref="BaseUrl"/>:
    /// <c>https://api.github.com/graphql</c>, or <c>https://ghe.example.com/api/graphql</c> for <c>…/api/v3</c>.
    /// </summary>
    public string? GraphQlUrl { get; set; }

    /// <summary>The configuration key that holds the personal access token. Never the token itself.</summary>
    public string? TokenConfigKey { get; set; }

    /// <summary>Resolved from <see cref="TokenConfigKey"/> at startup by Program.cs. Never serialised or logged.</summary>
    [JsonIgnore]
    public string? Token { get; set; }

    /// <summary>
    /// Sign in as a GitHub App instead of with <see cref="Token"/>: the review then posts as the app's
    /// bot (<c>&lt;slug&gt;[bot]</c>), which may approve PRs the token owner opened. Used when configured.
    /// </summary>
    public ReviewGitHubApp? App { get; set; }

    /// <summary>
    /// How update-branch brings a PR up to date: <c>rebase</c> (the default; no merge commit on the
    /// author's branch) or <c>merge</c> (GitHub's REST update-branch, which adds a merge commit).
    /// </summary>
    public string UpdateMethod { get; set; } = "rebase";

    public int TimeoutSeconds { get; set; } = 30;

    public override string ToString() => $"{Kind} {BaseUrl}";
}

/// <summary>
/// A GitHub App sign-in (<c>Review:CodeHosts:Hosts:&lt;id&gt;:App</c>). The adapter signs a short JWT with
/// the private key and exchanges it for an installation token (one hour), cached until near expiry.
/// </summary>
public sealed class ReviewGitHubApp
{
    public const string DefaultPrivateKeyConfigKey = "Review:Secrets:GitHubAppPrivateKey";

    /// <summary>The app's client id (<c>Iv23…</c>) or numeric app id: the JWT issuer.</summary>
    public string? AppId { get; set; }

    /// <summary>The installation to use. Absent, it is looked up per repo (<c>GET /repos/{repo}/installation</c>).</summary>
    public long? InstallationId { get; set; }

    /// <summary>The configuration key that holds the PEM private key. Never the key itself.</summary>
    public string? PrivateKeyConfigKey { get; set; }

    /// <summary>Resolved from <see cref="PrivateKeyConfigKey"/> at startup by Program.cs. Never serialised or logged.</summary>
    [JsonIgnore]
    public string? PrivateKey { get; set; }

    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(PrivateKey);
}

/// <summary>One repo's host: <see cref="Host"/> is a <see cref="ReviewCodeHostSettings.Hosts"/> id.</summary>
public sealed class ReviewCodeHostRepo
{
    /// <summary><c>owner/name</c>, as the intake request names it.</summary>
    public string Repo { get; set; } = "";

    /// <summary>The host id; absent means <c>github</c>, or <c>local</c> when <see cref="LocalPath"/> is set.</summary>
    public string? Host { get; set; }

    /// <summary>The repository directory for a local-git host.</summary>
    public string? LocalPath { get; set; }
}

/// <summary>The workflow executor (design 3.4), section <c>Review:Executor</c>.</summary>
public sealed class ReviewExecutorSettings
{
    public const string SectionName = "Review:Executor";

    public bool Enabled { get; set; } = true;

    /// <summary>How often the executor looks for work when nothing wakes it.</summary>
    public int PollSeconds { get; set; } = 10;

    /// <summary>
    /// A run left Running without an update for this long is taken over: its executor is presumed
    /// gone. Every node transition renews it.
    /// </summary>
    public int LeaseSeconds { get; set; } = 300;
}
