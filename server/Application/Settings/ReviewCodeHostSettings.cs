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
    public ReviewHost Kind { get; set; } = ReviewHost.GitHub;

    /// <summary>
    /// The REST API root: absent means <c>https://api.github.com</c>; GitHub Enterprise Server is
    /// <c>https://ghe.example.com/api/v3</c>. A company proxy goes here too.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The configuration key that holds the personal access token. Never the token itself.</summary>
    public string? TokenConfigKey { get; set; }

    /// <summary>Resolved from <see cref="TokenConfigKey"/> at startup by Program.cs. Never serialised or logged.</summary>
    [JsonIgnore]
    public string? Token { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    public override string ToString() => $"{Kind} {BaseUrl}";
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
