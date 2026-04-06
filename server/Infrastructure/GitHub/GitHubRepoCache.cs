using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.GitHub;

/// <summary>
/// In-memory cache of GitHub repositories. Fetched on first request and refreshable on demand.
/// Registered as singleton so the cache persists across requests.
/// </summary>
public class GitHubRepoCache
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GitHubRepoCache> _logger;
    private readonly GithubSettings _settings;

    private static readonly TimeSpan s_cacheTtl = TimeSpan.FromHours(24);

    private IReadOnlyList<GitHubRepoDto> _cached = [];
    private DateTime _cachedAt = DateTime.MinValue;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public GitHubRepoCache(
        IServiceProvider serviceProvider,
        IOptions<GithubSettings> settings,
        ILogger<GitHubRepoCache> logger)
    {
        _serviceProvider = serviceProvider;
        _settings = settings.Value;
        _logger = logger;
    }

    public bool IsEnabled => _settings.Enabled;
    public string BaseUrl => _settings.BaseUrl;

    public IReadOnlyList<GitHubRepoDto> GetCached() => _cached;

    public int RepoCount => _cached.Count;
    public DateTime? LastRefreshed => _cachedAt == DateTime.MinValue ? null : _cachedAt;
    public bool IsStale => DateTime.UtcNow - _cachedAt > s_cacheTtl;
    public TimeSpan CacheTtl => s_cacheTtl;

    public async Task<IReadOnlyList<GitHubRepoDto>> GetOrRefreshAsync(CancellationToken ct)
    {
        if (!_settings.Enabled)
            return [];

        if (!IsStale)
            return _cached;

        await _lock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (!IsStale)
                return _cached;

            await using var scope = _serviceProvider.CreateAsyncScope();
            var gitHubService = scope.ServiceProvider.GetRequiredService<IGitHubService>();
            _cached = await gitHubService.GetRepositoriesAsync(ct);
            _cachedAt = DateTime.UtcNow;
            return _cached;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh GitHub repository cache");
            return _cached; // Return stale data on error
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        if (!_settings.Enabled)
            return;

        await _lock.WaitAsync(ct);
        try
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var gitHubService = scope.ServiceProvider.GetRequiredService<IGitHubService>();
            _cached = await gitHubService.GetRepositoriesAsync(ct);
            _cachedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh GitHub repository cache");
        }
        finally
        {
            _lock.Release();
        }
    }
}
