using System.Collections.Concurrent;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Powers the working-directory autocomplete: lists drive roots for an empty input, and
/// otherwise reports whether the typed path exists plus the matching child directories.
///
/// Directory listings are cached per parent directory for a short TTL so keystroke-driven
/// requests don't hit disk every time. Uses an injected <see cref="TimeProvider"/> (not
/// <c>DateTime.UtcNow</c>) so cache expiry is deterministically testable with a fake clock.
/// Registered as a singleton so the cache persists across requests.
/// </summary>
public sealed class DirectoryBrowseService : IResettableCache
{
    private static readonly TimeSpan s_cacheTtl = TimeSpan.FromSeconds(15);

    private readonly IDirectoryLister _lister;
    private readonly IDriveProvider _driveProvider;
    private readonly TimeProvider _timeProvider;

    private readonly ConcurrentDictionary<string, CacheEntry> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private readonly record struct CacheEntry(IReadOnlyList<string> Children, DateTimeOffset CapturedAt);

    public DirectoryBrowseService(
        IDirectoryLister lister,
        IDriveProvider driveProvider,
        TimeProvider timeProvider)
    {
        _lister = lister;
        _driveProvider = driveProvider;
        _timeProvider = timeProvider;
    }

    public async Task<DirectoryBrowseResponse> BrowseAsync(string? path, CancellationToken ct)
    {
        var normalized = PathNormalizer.Normalize(path);

        if (normalized.Length == 0)
        {
            var drives = _driveProvider.GetDriveRoots();
            return new DirectoryBrowseResponse(NormalizedPath: string.Empty, Exists: false, IsDrivesListing: true, drives);
        }

        var exists = PathNormalizer.IsDriveRoot(normalized)
            ? _driveProvider.GetDriveRoots().Contains(normalized, StringComparer.OrdinalIgnoreCase)
            : _lister.DirectoryExists(normalized);

        // A trailing slash ("C:/src/") means "list this directory's children"; otherwise the
        // user is typing a partial name and we offer sibling matches from the parent directory.
        var descending = PathNormalizer.EndsWithSeparator(path);
        var listingDir = descending ? normalized : PathNormalizer.GetListingDirectory(normalized);
        var children = await GetChildrenCachedAsync(listingDir, ct);

        // Descending shows every child; typing a partial leaf ranks them by fuzzy/partial/BM25
        // relevance so e.g. "C:/src/lea" surfaces "C:/src/torquay-leander", not just prefix hits.
        var suggestions = descending
            ? children
            : DirectoryMatcher.Rank(PathNormalizer.GetLeaf(normalized), children);

        return new DirectoryBrowseResponse(normalized, exists, IsDrivesListing: false, suggestions);
    }

    private async Task<IReadOnlyList<string>> GetChildrenCachedAsync(string parentDir, CancellationToken ct)
    {
        if (parentDir.Length == 0)
            return [];

        if (TryGetFresh(parentDir, out var cached))
            return cached;

        await _refreshLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring the lock — another caller may have refreshed.
            if (TryGetFresh(parentDir, out cached))
                return cached;

            var children = _lister.ListDirectories(parentDir);
            _cache[parentDir] = new CacheEntry(children, _timeProvider.GetUtcNow());
            return children;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>Drops every cached directory listing. Used by tests sharing one app host so
    /// listings captured by an earlier test don't leak into a later one.</summary>
    public void Clear() => _cache.Clear();

    private bool TryGetFresh(string parentDir, out IReadOnlyList<string> children)
    {
        if (_cache.TryGetValue(parentDir, out var entry) &&
            _timeProvider.GetUtcNow() - entry.CapturedAt <= s_cacheTtl)
        {
            children = entry.Children;
            return true;
        }

        children = [];
        return false;
    }
}
