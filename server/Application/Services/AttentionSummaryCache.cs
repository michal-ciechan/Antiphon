using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace Antiphon.Server.Application.Services;

/// <summary>Small shared cache for the nav badge's counts-only attention projection.</summary>
public sealed class AttentionSummaryCache : IResettableCache
{
    private const string CacheKey = "attention:summary";
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(10);
    private readonly IMemoryCache _cache;

    public AttentionSummaryCache(IMemoryCache cache) => _cache = cache;

    public Task<AttentionSummaryDto> GetOrCreateAsync(Func<Task<AttentionSummaryDto>> factory) =>
        _cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Ttl;
            return await factory();
        })!;

    public void Clear() => _cache.Remove(CacheKey);
}
