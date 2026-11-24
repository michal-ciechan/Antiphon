using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Review;

/// <summary>
/// <see cref="IReviewGitCredentials"/> for GitHub hosts: the repo's <c>Review:CodeHosts</c> host (its
/// <c>Repos</c> row, else <c>github</c>), the web root from the host's API <c>BaseUrl</c>, and the App's
/// installation token (shared with <see cref="GitHubCodeHost"/> through <see cref="GitHubAppTokens"/>, so it
/// is minted once an hour), else the host's PAT. A singleton: one-shot reviewers fetch after the request
/// scope that started them has ended.
/// </summary>
public sealed class GitHubReviewGitCredentials(
    IHttpClientFactory httpFactory,
    IOptions<ReviewCodeHostSettings> settings,
    GitHubAppTokens appTokens) : IReviewGitCredentials
{
    public async Task<ReviewGitCredential> GetAsync(string repo, CancellationToken ct)
    {
        var options = settings.Value;
        var hostId = options.FindRepo(repo)?.Host is { Length: > 0 } named ? named : ReviewCodeHostSettings.DefaultGitHubHostId;
        if (!options.Hosts.TryGetValue(hostId, out var host) || host.Kind != ReviewHost.GitHub)
            throw new CodeHostException(CodeHostFailureKind.NotConfigured, $"GitHub host '{hostId}' for {repo} is not configured (Review:CodeHosts:Hosts).");

        var apiRoot = new Uri((string.IsNullOrWhiteSpace(host.BaseUrl) ? GitHubCodeHost.DefaultApiRoot : host.BaseUrl).TrimEnd('/') + "/");
        var token = host.App is { IsConfigured: true }
            ? await appTokens.GetTokenAsync(httpFactory.CreateClient(nameof(GitHubReviewGitCredentials)), hostId, host, apiRoot, repo.Trim(), ct)
            : host.Token;
        return new ReviewGitCredential(GitHubWebRoot.FromApiBaseUrl(host.BaseUrl), string.IsNullOrEmpty(token) ? null : token);
    }
}

/// <summary>The web (git) root of a GitHub host, from its REST API root.</summary>
public static class GitHubWebRoot
{
    public static readonly Uri Default = new("https://github.com/");

    /// <summary>
    /// None or <c>api.github.com</c>: <c>https://github.com/</c>. Enterprise Server's <c>https://h/api/v3</c>:
    /// <c>https://h/</c>. An <c>api.</c> host (GHE.com, <c>https://api.acme.ghe.com</c>): the host without
    /// <c>api.</c>. Anything else: its scheme, host and port.
    /// </summary>
    public static Uri FromApiBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var api))
            return Default;
        var host = api.Host.StartsWith("api.", StringComparison.OrdinalIgnoreCase) ? api.Host[4..] : api.Host;
        var path = api.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/api/v3", StringComparison.OrdinalIgnoreCase))
            path = path[..^"/api/v3".Length];
        else
            path = "";
        return new UriBuilder(api.Scheme, host, api.IsDefaultPort ? -1 : api.Port, path + "/").Uri;
    }
}
