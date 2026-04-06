using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.GitHub;

namespace Antiphon.Server.Api.Endpoints;

public static class GitHubEndpoints
{
    public static void MapGitHubEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/github")
            .WithTags("GitHub");

        group.MapGet("/status", async (
            GitHubRepoCache cache,
            IGitHubService gitHubService,
            CancellationToken cancellationToken) =>
        {
            var (connected, login, error) = cache.IsEnabled
                ? await gitHubService.CheckConnectivityAsync(cancellationToken)
                : (false, null, "GitHub integration is disabled");

            return Results.Ok(new
            {
                enabled = cache.IsEnabled,
                baseUrl = cache.BaseUrl,
                connected,
                authenticatedAs = login,
                error,
                repoCache = new
                {
                    count = cache.RepoCount,
                    lastRefreshed = cache.LastRefreshed,
                    isStale = cache.IsStale,
                    ttlMinutes = (int)cache.CacheTtl.TotalMinutes
                }
            });
        });

        group.MapGet("/repos", async (
            GitHubRepoCache cache,
            CancellationToken cancellationToken) =>
        {
            var repos = await cache.GetOrRefreshAsync(cancellationToken);
            return Results.Ok(repos);
        });

        group.MapPost("/repos/refresh", async (
            GitHubRepoCache cache,
            CancellationToken cancellationToken) =>
        {
            await cache.RefreshAsync(cancellationToken);
            return Results.Ok(cache.GetCached());
        });

        group.MapGet("/repos/{owner}/{repo}/branches", async (
            string owner,
            string repo,
            IGitHubService gitHubService,
            GitHubRepoCache cache,
            CancellationToken cancellationToken) =>
        {
            if (!cache.IsEnabled)
                return Results.Problem("GitHub integration is disabled.", statusCode: 503);

            var branches = await gitHubService.GetBranchesAsync(owner, repo, cancellationToken);
            return Results.Ok(branches);
        });
    }
}
