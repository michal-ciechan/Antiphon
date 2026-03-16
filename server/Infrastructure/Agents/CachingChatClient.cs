using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>
/// Controls how the CachingChatClient handles LLM call caching.
/// </summary>
public enum CacheMode
{
    /// <summary>Always call the LLM and overwrite any existing cache entry.</summary>
    Record,

    /// <summary>Use cache if available, otherwise call the LLM and cache the result.</summary>
    RecordIfMissing,

    /// <summary>Only use cached responses. Throws if cache miss.</summary>
    Replay,

    /// <summary>Always call the LLM, never read or write cache.</summary>
    PassThrough
}

/// <summary>
/// Represents a cached LLM interaction stored as JSON on disk.
/// </summary>
public sealed class CachedChatEntry
{
    public required string NormalizedRequestHash { get; init; }
    public required string OriginalRequest { get; init; }
    public required string Response { get; init; }
    public required DateTime CachedAt { get; init; }
}

/// <summary>
/// Decorator for IChatClient that caches LLM responses to disk.
/// Cache files are stored in the .antiphon-cache/ directory as one JSON file per cached call.
/// The content normalizer strips timestamps, absolute paths, UUIDs, and git SHAs
/// from messages before hashing to improve cache hit rates.
/// </summary>
public sealed partial class CachingChatClient
{
    private readonly string _cacheDirectory;
    private readonly CacheMode _mode;
    private readonly ILogger<CachingChatClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CachingChatClient(
        CacheMode mode,
        ILogger<CachingChatClient> logger,
        string? cacheDirectory = null)
    {
        _mode = mode;
        _logger = logger;
        _cacheDirectory = cacheDirectory ?? Path.Combine(Directory.GetCurrentDirectory(), ".antiphon-cache");

        if (_mode != CacheMode.PassThrough)
        {
            Directory.CreateDirectory(_cacheDirectory);
        }
    }

    /// <summary>
    /// Normalizes content by stripping volatile elements before hashing.
    /// This improves cache hit rates by removing timestamps, absolute paths, UUIDs, and git SHAs.
    /// </summary>
    public static string NormalizeContent(string content)
    {
        // Strip ISO 8601 timestamps (2026-03-16T12:34:56Z, 2026-03-16T12:34:56.789+00:00, etc.)
        var normalized = Iso8601Regex().Replace(content, "<TIMESTAMP>");

        // Strip common date/time formats (2026-03-16 12:34:56, Mar 16 2026, etc.)
        normalized = DateTimeRegex().Replace(normalized, "<DATETIME>");

        // Strip absolute Windows paths (C:\foo\bar, D:\something)
        normalized = WindowsPathRegex().Replace(normalized, "<PATH>");

        // Strip absolute Unix paths (/home/user/foo, /var/log/something)
        normalized = UnixPathRegex().Replace(normalized, "<PATH>");

        // Strip UUIDs (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)
        normalized = UuidRegex().Replace(normalized, "<UUID>");

        // Strip git SHAs (40-char hex strings)
        normalized = GitShaRegex().Replace(normalized, "<SHA>");

        return normalized;
    }

    /// <summary>
    /// Computes a SHA256 hash of the normalized content for use as a cache key.
    /// </summary>
    public static string ComputeHash(string normalizedContent)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedContent));
        return Convert.ToHexStringLower(hashBytes);
    }

    /// <summary>
    /// Attempts to read a cached response for the given request content.
    /// </summary>
    public async Task<string?> GetCachedResponseAsync(string requestContent, CancellationToken ct = default)
    {
        if (_mode == CacheMode.PassThrough || _mode == CacheMode.Record)
        {
            return null;
        }

        var normalized = NormalizeContent(requestContent);
        var hash = ComputeHash(normalized);
        var cacheFile = Path.Combine(_cacheDirectory, $"{hash}.json");

        if (!File.Exists(cacheFile))
        {
            if (_mode == CacheMode.Replay)
            {
                throw new InvalidOperationException(
                    $"Cache miss in Replay mode. No cached response found for hash '{hash}'. " +
                    $"Normalized content: {normalized[..Math.Min(200, normalized.Length)]}...");
            }

            _logger.LogDebug("Cache miss for hash {Hash}", hash);
            return null;
        }

        _logger.LogDebug("Cache hit for hash {Hash}", hash);
        var json = await File.ReadAllTextAsync(cacheFile, ct);
        var entry = JsonSerializer.Deserialize<CachedChatEntry>(json, JsonOptions);
        return entry?.Response;
    }

    /// <summary>
    /// Stores a response in the cache for the given request content.
    /// </summary>
    public async Task CacheResponseAsync(string requestContent, string response, CancellationToken ct = default)
    {
        if (_mode == CacheMode.PassThrough || _mode == CacheMode.Replay)
        {
            return;
        }

        var normalized = NormalizeContent(requestContent);
        var hash = ComputeHash(normalized);
        var cacheFile = Path.Combine(_cacheDirectory, $"{hash}.json");

        if (_mode == CacheMode.RecordIfMissing && File.Exists(cacheFile))
        {
            _logger.LogDebug("Cache entry already exists for hash {Hash}, skipping write", hash);
            return;
        }

        var entry = new CachedChatEntry
        {
            NormalizedRequestHash = hash,
            OriginalRequest = requestContent,
            Response = response,
            CachedAt = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(entry, JsonOptions);
        await File.WriteAllTextAsync(cacheFile, json, ct);
        _logger.LogDebug("Cached response for hash {Hash}", hash);
    }

    public CacheMode Mode => _mode;
    public string CacheDirectory => _cacheDirectory;

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})?")]
    private static partial Regex Iso8601Regex();

    [GeneratedRegex(@"(?:\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})|(?:(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+\d{1,2},?\s+\d{4})")]
    private static partial Regex DateTimeRegex();

    [GeneratedRegex(@"[A-Z]:\\[\w\\.-]+")]
    private static partial Regex WindowsPathRegex();

    [GeneratedRegex(@"(?<=/(?:home|var|tmp|usr|etc|opt|srv|mnt)/)\S+")]
    private static partial Regex UnixPathRegex();

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex UuidRegex();

    [GeneratedRegex(@"\b[0-9a-fA-F]{40}\b")]
    private static partial Regex GitShaRegex();
}
