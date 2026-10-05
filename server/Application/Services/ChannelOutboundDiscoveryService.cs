using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Fair, bounded historical source discovery in the existing outbound hosted loop. Cursors
/// are hints only: a fresh process wraps from the beginning and committed sources dedupe capture.
/// Durable trailing roots, definitive closure and TTL fencing follow in the later slices.
/// </summary>
public sealed class ChannelOutboundDiscoveryService(
    IServiceScopeFactory scopes, ChannelReplyDispatcher dispatcher,
    IOptions<ChannelOutboundSettings> settings, ILogger<ChannelOutboundDiscoveryService> logger)
{
    internal const int PageSize = 32;
    internal const int MaximumPages = 10;
    private DateTime? _afterCreatedAt;
    private Guid _afterId;
    private readonly Dictionary<Guid, long> _promptCursors = new();
    private readonly SemaphoreSlim _tickGate = new(1, 1);

    public async Task<int> TickAsync(CancellationToken ct)
    {
        if (!settings.Value.UnifiedRecoveryEnabled)
            return 0;
        await _tickGate.WaitAsync(ct);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var examined = 0;
            for (var page = 0; page < MaximumPages; page++)
            {
                var query = db.SessionQueuedMessages.AsNoTracking().Where(m =>
                    m.Status == QueuedMessageStatus.Sent && m.ChannelReplySettledAt == null
                    && m.ChannelOutboundDeliveryId == null && m.ChannelReplyDiscoveryClosedAt == null
                    && (m.Origin == QueuedMessageOrigin.Channel && m.ConversationKey != null
                        || m.Origin == QueuedMessageOrigin.Delegation || m.Origin == QueuedMessageOrigin.Check
                        || m.Origin == QueuedMessageOrigin.System || m.Origin == QueuedMessageOrigin.Scheduled));
                if (_afterCreatedAt is DateTime after)
                    query = query.Where(m => m.CreatedAt > after
                        || m.CreatedAt == after && m.Id.CompareTo(_afterId) > 0);
                var sources = await query.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
                    .Take(PageSize).ToListAsync(ct);
                if (sources.Count == 0)
                {
                    _afterCreatedAt = null;
                    break; // Wrap on the next tick, never examine a source twice in this cycle.
                }
                foreach (var source in sources)
                {
                    examined++;
                    try
                    {
                        var next = await dispatcher.DiscoverSourceAsync(source,
                            _promptCursors.GetValueOrDefault(source.Id), ct);
                        if (next is long sequence) _promptCursors[source.Id] = sequence;
                        else _promptCursors.Remove(source.Id);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogWarning(ex, "Outbound discovery failed for source {SourceId}", source.Id);
                    }
                    _afterCreatedAt = source.CreatedAt;
                    _afterId = source.Id;
                }
            }
            return examined;
        }
        finally { _tickGate.Release(); }
    }
}
