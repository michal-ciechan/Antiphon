using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Per-project readiness projections are expensive because they inspect the checkout. Keep each
/// one briefly, and give project writes one explicit place to invalidate it.
/// </summary>
public sealed class ProjectReadinessCache : IResettableCache
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _ttl;
    private readonly ConcurrentDictionary<Guid, byte> _knownProjectIds = new();

    public ProjectReadinessCache(IMemoryCache cache, IOptions<ProjectsSettings> settings)
    {
        _cache = cache;
        _ttl = TimeSpan.FromSeconds(Math.Max(1, settings.Value.ReadinessCacheSeconds));
    }

    public Task<ProjectReadinessDto> GetOrCreateAsync(
        Guid projectId,
        Func<Task<ProjectReadinessDto>> factory)
    {
        _knownProjectIds.TryAdd(projectId, 0);
        return _cache.GetOrCreateAsync(Key(projectId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _ttl;
            return await factory();
        })!;
    }

    public void Remove(Guid projectId)
    {
        _knownProjectIds.TryRemove(projectId, out _);
        _cache.Remove(Key(projectId));
    }

    public void Clear()
    {
        foreach (var projectId in _knownProjectIds.Keys)
            Remove(projectId);
    }

    private static string Key(Guid projectId) => $"projects:readiness:{projectId:N}";
}
