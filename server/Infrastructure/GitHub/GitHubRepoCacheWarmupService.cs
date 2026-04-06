namespace Antiphon.Server.Infrastructure.GitHub;

/// <summary>
/// Pre-warms the GitHub repository cache on startup so it's immediately available.
/// Runs once at startup; the cache's own TTL (24h) governs subsequent refreshes.
/// </summary>
public class GitHubRepoCacheWarmupService : IHostedService
{
    private readonly GitHubRepoCache _cache;
    private readonly ILogger<GitHubRepoCacheWarmupService> _logger;

    public GitHubRepoCacheWarmupService(GitHubRepoCache cache, ILogger<GitHubRepoCacheWarmupService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_cache.IsEnabled)
            return;

        _logger.LogInformation("Pre-warming GitHub repository cache on startup");
        try
        {
            await _cache.GetOrRefreshAsync(cancellationToken);
            _logger.LogInformation("GitHub repository cache warmed: {Count} repos", _cache.RepoCount);
        }
        catch (Exception ex)
        {
            // Don't crash startup — the cache will be populated on first request
            _logger.LogWarning(ex, "GitHub repository cache warmup failed; will retry on first request");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
